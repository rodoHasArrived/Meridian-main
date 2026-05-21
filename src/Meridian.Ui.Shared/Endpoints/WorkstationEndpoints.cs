using System.Globalization;
using System.Text.Json;
using Meridian.Application.Monitoring;
using Meridian.Application.OperationsContinuity;
using Meridian.Application.ProviderRouting;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.Auth;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.StrategyEngine;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Collectors;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.QuantScript.Compilation;
using Meridian.Storage.Export;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Shared.Workflows;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ContractSecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Endpoints for the desktop workstation API surface.
/// </summary>
public static partial class WorkstationEndpoints
{
    private const int MaxRunComparisonRequestIds = 10;
    private const int SecurityCoveragePreviewLimit = 5;
    private const string WorkstationApiRoutePrefix = "/api/workstation";

    public static void MapWorkstationEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/workstation").WithTags("Workstation");

        group.MapGet("/session", async (HttpContext context) =>
        {
            return await BuildSessionPayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationSession");

        group.MapGet("/research", async (HttpContext context) =>
        {
            return await BuildResearchPayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationResearch");

        group.MapGet("/strategy", async (HttpContext context) =>
        {
            return await BuildResearchPayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationStrategy");

        group.MapGet("/research/briefing", async (HttpContext context) =>
        {
            var briefing = await BuildResearchBriefingAsync(context).ConfigureAwait(false);
            return Results.Json(briefing, jsonOptions);
        })
        .WithName("GetWorkstationResearchBriefing")
        .Produces<ResearchBriefingDto>(200);

        group.MapGet("/strategy/briefing", async (HttpContext context) =>
        {
            var briefing = await BuildResearchBriefingAsync(context).ConfigureAwait(false);
            return Results.Json(briefing, jsonOptions);
        })
        .WithName("GetWorkstationStrategyBriefing")
        .Produces<ResearchBriefingDto>(200);

        MapStrategyDesignerEndpoints(group, jsonOptions);
        MapStrategyEngineEndpoints(group, jsonOptions);

        group.MapGet("/workflow-summary", async (
            bool? hasOperatingContext,
            string? operatingContext,
            string? fundProfileId,
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
                    fundDisplayName: fundDisplayName,
                    ct: context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(summary, jsonOptions);
        })
        .WithName("GetWorkstationWorkflowSummary")
        .Produces<OperatorWorkflowHomeSummary>(200)
        .Produces(501);

        group.MapGet("/workflows", (HttpContext context) =>
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

        group.MapGet("/workflows/presets", async (HttpContext context) =>
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

        group.MapPost("/workflows/presets", async (WorkflowPresetSaveRequest request, HttpContext context) =>
        {
            var service = context.RequestServices.GetService<WorkflowPresetService>();
            if (service is null)
            {
                return Results.Problem("Workflow preset service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var result = await service.SaveAsync(request, context.RequestAborted).ConfigureAwait(false);
            return result.Success
                ? Results.Json(result.Preset, jsonOptions)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("SaveWorkstationWorkflowPreset")
        .Produces<WorkflowPresetDto>(200)
        .Produces(400)
        .Produces(501);

        group.MapPut("/workflows/presets/{presetId}", async (
            string presetId,
            WorkflowPresetSaveRequest request,
            HttpContext context) =>
        {
            var service = context.RequestServices.GetService<WorkflowPresetService>();
            if (service is null)
            {
                return Results.Problem("Workflow preset service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var result = await service
                .SaveAsync(request with { PresetId = presetId }, context.RequestAborted)
                .ConfigureAwait(false);
            return result.Success
                ? Results.Json(result.Preset, jsonOptions)
                : Results.BadRequest(new { error = result.Error });
        })
        .WithName("UpdateWorkstationWorkflowPreset")
        .Produces<WorkflowPresetDto>(200)
        .Produces(400)
        .Produces(501);

        group.MapPost("/workflows/presets/{presetId}/pin", async (
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

        group.MapPost("/workflows/presets/{presetId}/used", async (string presetId, HttpContext context) =>
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

        group.MapDelete("/workflows/presets/{presetId}", async (string presetId, HttpContext context) =>
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

        group.MapGet("/trading", async (HttpContext context) =>
        {
            return await BuildTradingPayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationTrading");

        group.MapGet("/trading/readiness", async (Guid? fundAccountId, HttpContext context) =>
        {
            var readiness = await GetTradingOperatorReadinessAsync(fundAccountId, context).ConfigureAwait(false);
            return Results.Json(readiness, jsonOptions);
        })
        .WithName("GetWorkstationTradingReadiness")
        .Produces<TradingOperatorReadinessDto>(200);

        group.MapGet("/operator/inbox", async (Guid? fundAccountId, HttpContext context) =>
        {
            var inbox = await BuildOperatorInboxAsync(fundAccountId, context).ConfigureAwait(false);
            return Results.Json(inbox, jsonOptions);
        })
        .WithName("GetWorkstationOperatorInbox")
        .Produces<OperatorInboxDto>(200);

        group.MapGet("/data-operations", async (HttpContext context) =>
        {
            return await BuildDataOperationsPayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationDataOperations");

        group.MapGet("/data", async (HttpContext context) =>
        {
            return await BuildDataOperationsPayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationData");

        group.MapGet("/governance", async (HttpContext context) =>
        {
            return await BuildGovernancePayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationGovernance");

        group.MapGet("/accounting", async (HttpContext context) =>
        {
            return await BuildGovernancePayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationAccounting");

        group.MapGet("/reporting", async (HttpContext context) =>
        {
            return await BuildGovernancePayloadAsync(context).ConfigureAwait(false);
        })
        .WithName("GetWorkstationReporting");

        group.MapGet("/portfolio", async (HttpContext context) =>
        {
            var payload = await BuildPortfolioPayloadAsync(context).ConfigureAwait(false);
            return Results.Json(payload, jsonOptions);
        })
        .WithName("GetWorkstationPortfolio")
        .Produces<WorkstationPortfolioPayload>(200);


        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuity), async (
            Guid? fundAccountId,
            string? periodId,
            string? status,
            HttpContext context) =>
        {
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

            var workflows = await service.ListAsync(fundAccountId, periodId, parsedStatus, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(workflows, jsonOptions);
        })
        .WithName("GetOperationsContinuitySummary");

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
            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var workflow = await service.GetAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
            return workflow is null ? Results.NotFound() : Results.Json(workflow, jsonOptions);
        })
        .WithName("GetOperationsContinuityDetail");

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuityTimeline), async (Guid workflowId, HttpContext context) =>
        {
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

            var trustedRequest = request with { Actor = currentUser };
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

            var trustedRequest = request with { Actor = currentUser };
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
            var service = context.RequestServices.GetService<IOperationsContinuityWorkflowService>();
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var workflow = await service.GetAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
            return workflow is null ? Results.NotFound() : Results.Json(workflow.LedgerPreview, jsonOptions);
        })
        .WithName("GetOperationsContinuityLedgerPreview");


        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationRuns), async (ReconciliationRunRequest request, HttpContext context) =>
        {
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
        .WithName("CreateReconciliationRun")
        .Produces<ReconciliationRunDetail>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationRunById), async (string reconciliationRunId, HttpContext context) =>
        {
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

        group.MapGet("/reconciliation/break-queue", async (string? status, string? fundAccountId, HttpContext context) =>
        {
            await EnsureBreakQueueSeededAsync(context.RequestServices, context.RequestAborted).ConfigureAwait(false);
            var items = await GetBreakQueueItemsAsync(context.RequestServices, status, fundAccountId, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(items, jsonOptions);
        })
        .WithName("GetReconciliationBreakQueue")
        .Produces<IReadOnlyList<ReconciliationBreakQueueItem>>(200);

        group.MapGet("/reconciliation/break-queue/{breakId}", async (string breakId, HttpContext context) =>
        {
            await EnsureBreakQueueSeededAsync(context.RequestServices, context.RequestAborted).ConfigureAwait(false);
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

        group.MapGet("/reconciliation/calibration-summary", async (HttpContext context) =>
        {
            var asOf = DateTimeOffset.UtcNow;
            await EnsureBreakQueueSeededAsync(context.RequestServices, context.RequestAborted).ConfigureAwait(false);
            var items = await GetBreakQueueItemsAsync(context.RequestServices, status: null, fundAccountId: null, context.RequestAborted).ConfigureAwait(false);
            var summary = BuildReconciliationCalibrationSummary(items, asOf);
            return Results.Json(summary, jsonOptions);
        })
        .WithName("GetReconciliationCalibrationSummary")
        .Produces<ReconciliationCalibrationSummaryDto>(200);

        group.MapGet("/reconciliation/break-queue/{breakId}/audit", async (string breakId, HttpContext context) =>
        {
            await EnsureBreakQueueSeededAsync(context.RequestServices, context.RequestAborted).ConfigureAwait(false);
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

        group.MapPost("/reconciliation/break-queue/{breakId}/review", async (string breakId, ReviewReconciliationBreakRequest request, HttpContext context) =>
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

            var trustedRequest = request with { ReviewedBy = currentUser };

            await EnsureBreakQueueSeededAsync(context.RequestServices, context.RequestAborted).ConfigureAwait(false);
            var transition = await ReviewBreakAsync(context.RequestServices, trustedRequest, context.RequestAborted).ConfigureAwait(false);
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

        group.MapPost("/reconciliation/break-queue/{breakId}/resolve", async (string breakId, ResolveReconciliationBreakRequest request, HttpContext context) =>
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

            if (!TryResolveCurrentUser(context, out var currentUser))
            {
                return Results.Unauthorized();
            }

            var trustedRequest = request with { ResolvedBy = currentUser };

            await EnsureBreakQueueSeededAsync(context.RequestServices, context.RequestAborted).ConfigureAwait(false);
            var transition = await ResolveBreakAsync(context.RequestServices, trustedRequest, context.RequestAborted).ConfigureAwait(false);
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

        group.MapGet("/runs/{runId}/ledger", async (string runId, HttpContext context) =>
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

        group.MapGet("/runs/{runId}/continuity", async (string runId, HttpContext context) =>
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

        group.MapGet("/runs/{runId}/review-packet", async (string runId, Guid? fundAccountId, HttpContext context) =>
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

        group.MapGet("/runs/{runId}/equity-curve", async (string runId, HttpContext context) =>
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

        group.MapGet("/runs/{runId}/fills", async (string runId, string? symbol, HttpContext context) =>
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

        group.MapGet("/runs/{runId}/attribution", async (string runId, HttpContext context) =>
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

        group.MapGet("/runs/{runId}/ledger/trial-balance", async (string runId, string? accountType, HttpContext context) =>
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
            return Results.Json(lines, jsonOptions);
        })
        .WithName("GetRunLedgerTrialBalance")
        .Produces<IReadOnlyList<LedgerTrialBalanceLine>>(200)
        .Produces(404);

        group.MapGet("/runs/{runId}/ledger/journal", async (
            string runId,
            DateTimeOffset? from,
            DateTimeOffset? to,
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

            return Results.Json(entries.ToArray(), jsonOptions);
        })
        .WithName("GetRunLedgerJournal")
        .Produces<IReadOnlyList<LedgerJournalLine>>(200)
        .Produces(404);

        group.MapGet("/security-master/securities", async (
            string? query,
            int? take,
            bool activeOnly,
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
                ActiveOnly: activeOnly);
            var results = await queryService.SearchAsync(request, ct).ConfigureAwait(false);
            return Results.Json(results.Select(MapToWorkstationSecurity).ToArray(), jsonOptions);
        })
        .WithName("SearchSecurityMasterWorkstation")
        .Produces<IReadOnlyList<SecurityMasterWorkstationDto>>(200)
        .Produces(400);

        group.MapGet("/security-master/securities/{securityId:guid}", async (
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

        group.MapPost("/runs/compare", async (RunComparisonRequest request, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            if (request.RunIds is not { Count: >= 2 })
            {
                return Results.BadRequest(new { error = "At least two run IDs are required for comparison." });
            }

            if (request.RunIds.Count > MaxRunComparisonRequestIds)
            {
                return Results.BadRequest(new { error = $"A maximum of {MaxRunComparisonRequestIds} run IDs can be compared per request." });
            }

            var comparison = await readService.CompareRunsAsync(request.RunIds, context.RequestAborted).ConfigureAwait(false);
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

        group.MapPost("/runs/diff", async (RunDiffRequest request, HttpContext context) =>
        {
            var readService = context.RequestServices.GetService<StrategyRunReadService>();
            if (readService is null)
            {
                return Results.Problem("Strategy run service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var baseDetail = await readService.GetRunDetailAsync(request.BaseRunId, context.RequestAborted).ConfigureAwait(false);
            var targetDetail = await readService.GetRunDetailAsync(request.TargetRunId, context.RequestAborted).ConfigureAwait(false);

            if (baseDetail is null || targetDetail is null)
            {
                return Results.NotFound(new { error = "One or both run IDs not found." });
            }

            var diff = BuildRunDiff(baseDetail, targetDetail);
            return Results.Json(diff, jsonOptions);
        })
        .WithName("DiffRuns")
        .Produces<StrategyRunDiff>(200)
        .Produces(404)
        .Produces(501);

        app.MapGet("/api/strategies/{strategyId}/runs", async (string strategyId, string? type, HttpContext context) =>
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

        group.MapGet("/runs/history", async (
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

        group.MapGet("/runs/timeline", async (
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

        group.MapGet("/runs/lineage-timeline", async (
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
            return Results.Json(timeline, jsonOptions);
        })
        .WithName("GetWorkstationRunLineageTimeline")
        .Produces<IReadOnlyList<StrategyRunLineageTimelineEntry>>(200)
        .Produces(501);

        group.MapGet("/runs/sweeps", async (int? limit, HttpContext context) =>
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

        app.MapGet("/api/strategies/runs/compare", async (string? ids, HttpContext context) =>
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

        portfolioGroup.MapGet("/{runId}/cash-flows", async (
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

        portfolioGroup.MapGet("/aggregate", (HttpContext context) =>
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

        portfolioGroup.MapGet("/exposure", (HttpContext context) =>
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

        portfolioGroup.MapGet("/symbols/{symbol}/exposure", (string symbol, HttpContext context) =>
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

    private static StrategyRunDiff BuildRunDiff(StrategyRunDetail baseRun, StrategyRunDetail targetRun)
    {
        var basePositions = baseRun.Portfolio?.Positions ?? [];
        var targetPositions = targetRun.Portfolio?.Positions ?? [];

        var baseSymbols = new HashSet<string>(basePositions.Select(static p => p.Symbol), StringComparer.OrdinalIgnoreCase);
        var targetSymbols = new HashSet<string>(targetPositions.Select(static p => p.Symbol), StringComparer.OrdinalIgnoreCase);

        var added = targetPositions
            .Where(p => !baseSymbols.Contains(p.Symbol))
            .Select(static p => new PositionDiffEntry(p.Symbol, 0, p.Quantity, 0m, p.RealizedPnl + p.UnrealizedPnl, "Added"))
            .ToList();

        var removed = basePositions
            .Where(p => !targetSymbols.Contains(p.Symbol))
            .Select(static p => new PositionDiffEntry(p.Symbol, p.Quantity, 0, p.RealizedPnl + p.UnrealizedPnl, 0m, "Removed"))
            .ToList();

        var modified = new List<PositionDiffEntry>();
        foreach (var basePos in basePositions.Where(p => targetSymbols.Contains(p.Symbol)))
        {
            var targetPos = targetPositions.First(p =>
                string.Equals(p.Symbol, basePos.Symbol, StringComparison.OrdinalIgnoreCase));
            if (basePos.Quantity != targetPos.Quantity ||
                basePos.AverageCostBasis != targetPos.AverageCostBasis)
            {
                modified.Add(new PositionDiffEntry(
                    basePos.Symbol,
                    basePos.Quantity,
                    targetPos.Quantity,
                    basePos.RealizedPnl + basePos.UnrealizedPnl,
                    targetPos.RealizedPnl + targetPos.UnrealizedPnl,
                    "Modified"));
            }
        }

        var paramDiffs = BuildParameterDiff(baseRun.Parameters, targetRun.Parameters);

        var metricsDiff = new MetricsDiff(
            NetPnlDelta: (targetRun.Summary.NetPnl ?? 0m) - (baseRun.Summary.NetPnl ?? 0m),
            TotalReturnDelta: (targetRun.Summary.TotalReturn ?? 0m) - (baseRun.Summary.TotalReturn ?? 0m),
            FillCountDelta: targetRun.Summary.FillCount - baseRun.Summary.FillCount,
            BaseNetPnl: baseRun.Summary.NetPnl,
            TargetNetPnl: targetRun.Summary.NetPnl,
            BaseTotalReturn: baseRun.Summary.TotalReturn,
            TargetTotalReturn: targetRun.Summary.TotalReturn);

        return new StrategyRunDiff(
            BaseRunId: baseRun.Summary.RunId,
            TargetRunId: targetRun.Summary.RunId,
            BaseStrategyName: baseRun.Summary.StrategyName,
            TargetStrategyName: targetRun.Summary.StrategyName,
            AddedPositions: added,
            RemovedPositions: removed,
            ModifiedPositions: modified,
            ParameterChanges: paramDiffs,
            Metrics: metricsDiff);
    }

    private static string WorkstationSubroute(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return route.StartsWith(WorkstationApiRoutePrefix, StringComparison.Ordinal)
            ? route[WorkstationApiRoutePrefix.Length..]
            : route;
    }

    private static IReadOnlyList<ParameterDiff> BuildParameterDiff(
        IReadOnlyDictionary<string, string> baseParams,
        IReadOnlyDictionary<string, string> targetParams)
    {
        var diffs = new List<ParameterDiff>();
        var allKeys = new HashSet<string>(baseParams.Keys.Concat(targetParams.Keys), StringComparer.Ordinal);

        foreach (var key in allKeys.Order())
        {
            baseParams.TryGetValue(key, out var baseVal);
            targetParams.TryGetValue(key, out var targetVal);

            if (!string.Equals(baseVal, targetVal, StringComparison.Ordinal))
            {
                diffs.Add(new ParameterDiff(key, baseVal, targetVal));
            }
        }

        return diffs;
    }

    // PR-03: returns typed DTO instead of anonymous object
    private static async Task<WorkstationSessionPayload> BuildSessionPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return new WorkstationSessionPayload(
                DisplayName: "Meridian Operator",
                Role: "Research Lead",
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
    private static async Task<WorkstationResearchPayload> BuildResearchPayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return BuildResearchFallbackPayload();
        }

        var runs = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false))
            .Take(6)
            .ToArray();
        var runDetails = await Task.WhenAll(
                runs.Select(run => readService.GetRunDetailAsync(run.RunId, context.RequestAborted)))
            .ConfigureAwait(false);

        if (runs.Length == 0)
        {
            return new WorkstationResearchPayload(
                Metrics:
                [
                    new WorkstationMetricCard("active-runs", "Active Runs", "0", "0%", "success"),
                    new WorkstationMetricCard("queued-runs", "Queued Promotions", "0", "0%", "default"),
                    new WorkstationMetricCard("review-runs", "Needs Review", "0", "0%", "warning"),
                    new WorkstationMetricCard("winning-runs", "Positive P&L", "0", "0%", "default")
                ],
                Runs: Array.Empty<object>(),
                Comparisons: Array.Empty<WorkstationModeComparisonGroup>(),
                Timeline: Array.Empty<WorkstationTimelineCard>(),
                Workspace: new WorkstationResearchWorkspaceSummary(0, null, null, false, false, 0),
                PlotTool: BuildResearchPlotToolPayload(Array.Empty<StrategyRunSummary>(), selectedRunIds: Array.Empty<string>()));
        }

        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var queuedPromotions = runs.Count(static run => run.Promotion is { RequiresReview: true } &&
            run.Promotion.State is StrategyRunPromotionState.CandidateForPaper or StrategyRunPromotionState.CandidateForLive);
        var reviewRuns = runs.Count(static run => run.Promotion?.RequiresReview == true || run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled);
        var winningRuns = runs.Count(static run => (run.NetPnl ?? 0m) > 0m);
        var latestRun = runs[0];

        return new WorkstationResearchPayload(
            Metrics:
            [
                new WorkstationMetricCard("active-runs", "Active Runs", activeRuns.ToString(CultureInfo.InvariantCulture), activeRuns == 0 ? "0%" : $"+{activeRuns}", "success"),
                new WorkstationMetricCard("queued-runs", "Queued Promotions", queuedPromotions.ToString(CultureInfo.InvariantCulture), queuedPromotions == 0 ? "0%" : $"+{queuedPromotions}", "default"),
                new WorkstationMetricCard("review-runs", "Needs Review", reviewRuns.ToString(CultureInfo.InvariantCulture), reviewRuns == 0 ? "0%" : $"-{reviewRuns}", "warning"),
                new WorkstationMetricCard("winning-runs", "Positive P&L", winningRuns.ToString(CultureInfo.InvariantCulture), winningRuns == 0 ? "0%" : $"+{winningRuns}", "default")
            ],
            Runs: runs
                .Zip(runDetails, static (run, detail) => BuildResearchRunCard(run, detail))
                .ToArray(),
            Comparisons: BuildModeComparisons(runs),
            Timeline: runs.Select(BuildTimelineCard).ToArray(),
            Workspace: new WorkstationResearchWorkspaceSummary(
                TotalRuns: runs.Length,
                LatestRunId: latestRun.RunId,
                LatestStrategyName: latestRun.StrategyName,
                HasLedgerCoverage: runs.Any(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                HasPortfolioCoverage: runs.Any(static run => !string.IsNullOrWhiteSpace(run.PortfolioId)),
                PromotionCandidates: queuedPromotions),
            PlotTool: BuildResearchPlotToolPayload(runs, selectedRunIds: Array.Empty<string>()));
    }

    private static async Task<ResearchBriefingDto> BuildResearchBriefingAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return BuildResearchBriefingFallback();
        }

        var runs = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false))
            .Take(10)
            .ToArray();
        var details = await Task.WhenAll(
                runs.Select(run => readService.GetRunDetailAsync(run.RunId, context.RequestAborted)))
            .ConfigureAwait(false);

        return BuildResearchBriefingFromRuns(runs, details);
    }

    private static ResearchBriefingDto BuildResearchBriefingFromRuns(
        IReadOnlyList<StrategyRunSummary> runs,
        IReadOnlyList<StrategyRunDetail?> details)
    {
        var activeRuns = runs.Count(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused);
        var promotionCandidates = runs.Count(static run => run.Promotion is { RequiresReview: true } &&
            run.Promotion.State is StrategyRunPromotionState.CandidateForPaper or StrategyRunPromotionState.CandidateForLive);
        var positivePnlRuns = runs.Count(static run => (run.NetPnl ?? 0m) > 0m);
        var latestRun = runs.FirstOrDefault();
        var alertItems = BuildBriefingAlerts(runs, details);

        return new ResearchBriefingDto(
            Workspace: new ResearchBriefingWorkspaceSummary(
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
                    : $"{activeRuns} active research session(s), {promotionCandidates} promotion candidate(s), and {alertItems.Count} alert(s) on the desk."),
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

    private static ResearchBriefingDto BuildResearchBriefingFallback()
    {
        var generatedAt = DateTimeOffset.UtcNow;
        return new ResearchBriefingDto(
            Workspace: new ResearchBriefingWorkspaceSummary(
                TotalRuns: 24,
                ActiveRuns: 6,
                PromotionCandidates: 3,
                PositivePnlRuns: 17,
                LatestRunId: "run-research-001",
                LatestStrategyName: "Mean Reversion FX",
                HasLedgerCoverage: true,
                HasPortfolioCoverage: true,
                Summary: "Research is organized around briefing context first, then run studio drill-ins."),
            InsightFeed: new InsightFeed(
                FeedId: "research-market-briefing",
                Title: "Pinned Insights",
                Summary: "A compact market briefing with pinned research tiles, saved comparisons, and promotion posture.",
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
                        RunId: "run-research-001",
                        DrillInRoute: RunRoute(UiApiRoutes.RunsEquityCurve, "run-research-001")),
                    new InsightWidget(
                        WidgetId: "insight-index-carry",
                        Title: "Index Carry Basket",
                        Subtitle: "Backtest · Completed",
                        Headline: "+2.8%",
                        Tone: "default",
                        Summary: "Pinned chart compares carry spread compression against basket returns.",
                        RunId: "run-research-014",
                        DrillInRoute: RunRoute(UiApiRoutes.RunsEquityCurve, "run-research-014")),
                    new InsightWidget(
                        WidgetId: "insight-vol-breakout",
                        Title: "Volatility Breakout",
                        Subtitle: "Backtest · Needs review",
                        Headline: "-0.9%",
                        Tone: "warning",
                        Summary: "Transaction-cost preview deteriorated after the most recent parameter sweep.",
                        RunId: "run-research-022",
                        DrillInRoute: RunRoute(UiApiRoutes.RunsEquityCurve, "run-research-022"))
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
                new ResearchBriefingRun(
                    RunId: "run-research-001",
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
                    DrillIn: new ResearchRunDrillInLinks(
                        EquityCurve: RunRoute(UiApiRoutes.RunsEquityCurve, "run-research-001"),
                        Fills: RunRoute(UiApiRoutes.RunsFills, "run-research-001"),
                        Attribution: RunRoute(UiApiRoutes.RunsAttribution, "run-research-001"),
                        Ledger: RunRoute(UiApiRoutes.RunsLedger, "run-research-001"),
                        CashFlows: RunRoute(UiApiRoutes.PortfolioCashFlows, "run-research-001"),
                        Continuity: RunRoute(UiApiRoutes.RunsContinuity, "run-research-001")))
            ],
            SavedComparisons:
            [
                new ResearchSavedComparison(
                    ComparisonId: "cmp-meanrev-fx",
                    StrategyName: "Mean Reversion FX",
                    ModeSummary: "Backtest -> Paper",
                    Summary: "Saved compare lane tracks readiness from completed backtest into paper execution.",
                    AnchorRunId: "run-research-001",
                    Modes:
                    [
                        new ResearchSavedComparisonMode(
                            RunId: "run-research-001",
                            Mode: StrategyRunMode.Paper,
                            Status: StrategyRunStatus.Running,
                            NetPnl: 4200m,
                            TotalReturn: 0.042m,
                            DrillIn: new ResearchRunDrillInLinks(
                                EquityCurve: RunRoute(UiApiRoutes.RunsEquityCurve, "run-research-001"),
                                Fills: RunRoute(UiApiRoutes.RunsFills, "run-research-001"),
                                Attribution: RunRoute(UiApiRoutes.RunsAttribution, "run-research-001"),
                                Ledger: RunRoute(UiApiRoutes.RunsLedger, "run-research-001"),
                                CashFlows: RunRoute(UiApiRoutes.PortfolioCashFlows, "run-research-001"),
                                Continuity: RunRoute(UiApiRoutes.RunsContinuity, "run-research-001")))
                    ])
            ],
            Alerts:
            [
                new ResearchBriefingAlert(
                    AlertId: "alert-promotion-review",
                    Title: "Promotion review due",
                    Summary: "Mean Reversion FX is running in paper and is queued for live promotion review.",
                    Tone: "warning",
                    RunId: "run-research-001",
                    ActionLabel: "Review run"),
                new ResearchBriefingAlert(
                    AlertId: "alert-cost-preview",
                    Title: "Execution costs widened",
                    Summary: "Volatility Breakout now shows a weaker transaction-cost preview than the prior saved comparison.",
                    Tone: "default",
                    RunId: "run-research-022",
                    ActionLabel: "Open comparison")
            ],
            WhatChanged:
            [
                new ResearchWhatChangedItem(
                    ChangeId: "change-paper-ready",
                    Title: "Paper lane updated",
                    Summary: "Mean Reversion FX stayed profitable and kept full ledger continuity after the latest refresh.",
                    Category: "paper",
                    Timestamp: generatedAt.AddMinutes(-2),
                    RelativeTime: "2m ago",
                    RunId: "run-research-001"),
                new ResearchWhatChangedItem(
                    ChangeId: "change-backtest-failed",
                    Title: "Backtest needs review",
                    Summary: "Volatility Breakout completed with weaker returns and is now flagged for review.",
                    Category: "review",
                    Timestamp: generatedAt.AddMinutes(-18),
                    RelativeTime: "18m ago",
                    RunId: "run-research-022")
            ]);
    }

    // PR-03: returns typed DTO
    private static WorkstationResearchPayload BuildResearchFallbackPayload()
    {
        return new WorkstationResearchPayload(
            Metrics:
            [
                new WorkstationMetricCard("active-runs", "Active Runs", "24", "+8%", "success"),
                new WorkstationMetricCard("queued-runs", "Queued Promotions", "3", "0%", "default"),
                new WorkstationMetricCard("review-runs", "Needs Review", "2", "-1%", "warning"),
                new WorkstationMetricCard("winning-runs", "Positive P&L", "17", "+4%", "default")
            ],
            Runs:
            [
                new
                {
                    id = "run-research-001",
                    strategyName = "Mean Reversion FX",
                    engine = "Meridian Native",
                    mode = "paper",
                    status = "Running",
                    dataset = "FX Majors",
                    window = "90d",
                    pnl = "+4.2%",
                    sharpe = "1.41",
                    lastUpdated = "2m ago",
                    notes = "Primary paper candidate with stable fill quality and healthy depth coverage.",
                    securityCoverage = new
                    {
                        portfolioResolved = 0,
                        portfolioMissing = 0,
                        ledgerResolved = 0,
                        ledgerMissing = 0,
                        hasIssues = false,
                        tone = "default",
                        summary = "Security Master coverage not yet evaluated.",
                        resolvedReferences = Array.Empty<SecurityCoverageReferencePayload>(),
                        missingReferences = Array.Empty<SecurityCoverageGapPayload>()
                    }
                }
            ],
            Comparisons: Array.Empty<WorkstationModeComparisonGroup>(),
            Timeline: Array.Empty<WorkstationTimelineCard>(),
            Workspace: new WorkstationResearchWorkspaceSummary(1, "run-research-001", "Mean Reversion FX", false, false, 0),
            PlotTool: BuildResearchFallbackPlotToolPayload());
    }

    // PR-03: returns typed DTO instead of anonymous object
    private static async Task<WorkstationTradingPayload> BuildTradingPayloadAsync(HttpContext context)
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

        var readiness = await GetTradingOperatorReadinessAsync(null, context).ConfigureAwait(false);

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

    private static async Task<OperatorInboxDto> BuildOperatorInboxAsync(Guid? fundAccountId, HttpContext context)
    {
        var asOf = DateTimeOffset.UtcNow;
        var readiness = await GetTradingOperatorReadinessAsync(fundAccountId, context).ConfigureAwait(false);
        var workItems = readiness.WorkItems
            .Select(AttachOperatorNavigation)
            .ToList();

        await AddRunReviewPacketWorkItemsAsync(context, fundAccountId, workItems, asOf).ConfigureAwait(false);
        await AddReconciliationBreakWorkItemsAsync(context, workItems, asOf).ConfigureAwait(false);
        var operatorInbox = context.RequestServices.GetService<IOperatorInboxService>();
        if (operatorInbox is not null)
        {
            var contributedItems = await operatorInbox.GetItemsAsync(context.RequestAborted).ConfigureAwait(false);
            workItems.AddRange(contributedItems.Select(AttachOperatorNavigation));
        }

        var items = workItems
            .GroupBy(static item => item.WorkItemId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderByDescending(static item => item.Tone)
                .ThenByDescending(static item => item.CreatedAt)
                .First())
            .OrderByDescending(static item => item.Tone)
            .ThenByDescending(static item => item.CreatedAt)
            .ThenBy(static item => item.WorkItemId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        RecordOperatorInboxContinuityMetrics(items);

        var criticalCount = items.Count(static item => item.Tone == OperatorWorkItemToneDto.Critical);
        var warningCount = items.Count(static item => item.Tone == OperatorWorkItemToneDto.Warning);
        var reviewCount = criticalCount + warningCount;

        return new OperatorInboxDto(
            AsOf: asOf,
            Items: items,
            CriticalCount: criticalCount,
            WarningCount: warningCount,
            ReviewCount: reviewCount,
            Summary: BuildOperatorInboxSummary(items, criticalCount, warningCount));
    }

    private static void RecordOperatorInboxContinuityMetrics(IReadOnlyList<OperatorWorkItemDto> items)
    {
        foreach (var item in items)
        {
            if (item.Tone is not (OperatorWorkItemToneDto.Warning or OperatorWorkItemToneDto.Critical))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Workspace)
                || string.IsNullOrWhiteSpace(item.TargetRoute)
                || string.IsNullOrWhiteSpace(item.TargetPageTag))
            {
                var failureKind = string.IsNullOrWhiteSpace(item.WorkItemId)
                    ? "missing-navigation"
                    : item.WorkItemId;
                PrometheusMetrics.RecordRunContinuityUnresolvedBlockerLinkage("operator-inbox", failureKind);
            }
        }
    }

    private static async Task AddRunReviewPacketWorkItemsAsync(
        HttpContext context,
        Guid? fundAccountId,
        List<OperatorWorkItemDto> workItems,
        DateTimeOffset asOf)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        var reviewPacketService = context.RequestServices.GetService<StrategyRunReviewPacketService>();
        if (readService is null || reviewPacketService is null)
        {
            return;
        }

        try
        {
            var runs = await readService
                .GetRunsAsync(new StrategyRunHistoryQuery(Limit: 6), context.RequestAborted)
                .ConfigureAwait(false);

            var reviewRuns = runs
                .Where(ShouldSurfaceRunReviewWorkItems)
                .OrderByDescending(GetRunReviewTimestamp)
                .ToArray();
            var latestReviewRunId = reviewRuns.FirstOrDefault()?.RunId;

            foreach (var run in reviewRuns)
            {
                var packet = await reviewPacketService
                    .GetAsync(run.RunId, fundAccountId, context.RequestAborted)
                    .ConfigureAwait(false);
                if (packet is null)
                {
                    continue;
                }

                var isLatestReviewRun = string.Equals(run.RunId, latestReviewRunId, StringComparison.OrdinalIgnoreCase);
                var hasNonPromotionAttention = packet.WorkItems.Any(static item =>
                    item.Kind != OperatorWorkItemKindDto.PromotionReview &&
                    item.Tone is OperatorWorkItemToneDto.Warning or OperatorWorkItemToneDto.Critical);
                workItems.AddRange(packet.WorkItems
                    .Where(item =>
                        item.Tone is OperatorWorkItemToneDto.Warning or OperatorWorkItemToneDto.Critical &&
                        (isLatestReviewRun ||
                         item.Kind != OperatorWorkItemKindDto.PromotionReview ||
                         !hasNonPromotionAttention))
                    .Select(AttachOperatorNavigation));
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            workItems.Add(BuildRunReviewPacketUnavailableWorkItem(asOf));
        }
    }

    private static bool ShouldSurfaceRunReviewWorkItems(StrategyRunSummary run)
        => run.Promotion?.RequiresReview == true ||
           run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled;

    private static DateTimeOffset GetRunReviewTimestamp(StrategyRunSummary run)
        => run.CompletedAt ?? run.LastUpdatedAt;

    private static OperatorWorkItemDto BuildRunReviewPacketUnavailableWorkItem(DateTimeOffset asOf)
        => new(
            WorkItemId: "run-review-packets-unavailable",
            Kind: OperatorWorkItemKindDto.PromotionReview,
            Label: "Run review packets unavailable",
            Detail: "Trading readiness is still available, but run review-packet work items could not be loaded. Review run-read service health before accepting promotion queue coverage.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: asOf,
            Workspace: "Trading",
            TargetRoute: UiApiRoutes.WorkstationOperatorInbox,
            TargetPageTag: "TradingShell");

    private static async Task AddReconciliationBreakWorkItemsAsync(
        HttpContext context,
        List<OperatorWorkItemDto> workItems,
        DateTimeOffset asOf)
    {
        try
        {
            await EnsureBreakQueueSeededAsync(context.RequestServices, context.RequestAborted).ConfigureAwait(false);
            var reconciliationBreaks = await GetBreakQueueItemsAsync(
                context.RequestServices,
                status: null,
                fundAccountId: null,
                context.RequestAborted).ConfigureAwait(false);
            workItems.AddRange(reconciliationBreaks
                .Where(static item => item.Status is ReconciliationBreakQueueStatus.Open or ReconciliationBreakQueueStatus.InReview)
                .Select(MapReconciliationBreakWorkItem));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            workItems.Add(BuildReconciliationBreakQueueUnavailableWorkItem(asOf));
        }
    }

    private static OperatorWorkItemDto BuildReconciliationBreakQueueUnavailableWorkItem(DateTimeOffset asOf)
        => new(
            WorkItemId: "reconciliation-break-queue-unavailable",
            Kind: OperatorWorkItemKindDto.ReconciliationBreak,
            Label: "Reconciliation queue unavailable",
            Detail: "Trading readiness is still available, but reconciliation break work items could not be loaded. Review storage health before accepting accounting queue coverage.",
            Tone: OperatorWorkItemToneDto.Warning,
            CreatedAt: asOf,
            Workspace: "Accounting",
            TargetRoute: UiApiRoutes.ReconciliationBreakQueue,
            TargetPageTag: "AccountingShell");

    private static OperatorWorkItemDto AttachOperatorNavigation(OperatorWorkItemDto item)
    {
        var navigation = ResolveOperatorNavigation(item.Kind, item.FundAccountId);
        return item with
        {
            Workspace = item.Workspace ?? navigation.Workspace,
            TargetRoute = item.TargetRoute ?? navigation.TargetRoute,
            TargetPageTag = item.TargetPageTag ?? navigation.TargetPageTag
        };
    }

    private static OperatorWorkItemDto MapReconciliationBreakWorkItem(ReconciliationBreakQueueItem item)
    {
        var tone = item.Severity switch
        {
            ReconciliationBreakSeverity.Critical => OperatorWorkItemToneDto.Critical,
            ReconciliationBreakSeverity.High or ReconciliationBreakSeverity.Medium => OperatorWorkItemToneDto.Warning,
            _ => OperatorWorkItemToneDto.Info
        };
        var assignment = string.IsNullOrWhiteSpace(item.AssignedTo)
            ? "unassigned"
            : $"assigned to {item.AssignedTo}";
        var status = item.Status == ReconciliationBreakQueueStatus.InReview
            ? "in review"
            : "open";
        var routeDetail = BuildReconciliationRoutingDetail(item);

        return new OperatorWorkItemDto(
            WorkItemId: BuildOperatorInboxScopedId("reconciliation-break", item.BreakId),
            Kind: OperatorWorkItemKindDto.ReconciliationBreak,
            Label: item.Status == ReconciliationBreakQueueStatus.InReview
                ? "Reconciliation break in review"
                : "Reconciliation break requires review",
            Detail: $"{item.StrategyName}: {item.Reason} The break is {status} and {assignment}. {routeDetail}",
            Tone: tone,
            CreatedAt: item.DetectedAt,
            RunId: item.RunId,
            AuditReference: item.BreakId,
            Workspace: "Accounting",
            TargetRoute: UiApiRoutes.ReconciliationBreakQueue,
            TargetPageTag: "AccountingShell");
    }

    private static string BuildReconciliationRoutingDetail(ReconciliationBreakQueueItem item)
    {
        var exceptionRoute = string.IsNullOrWhiteSpace(item.ExceptionRoute)
            ? "operations-triage"
            : item.ExceptionRoute;
        var toleranceProfileId = string.IsNullOrWhiteSpace(item.ToleranceProfileId)
            ? "standard-recon-tolerance"
            : item.ToleranceProfileId;
        var requiredSignoffRole = string.IsNullOrWhiteSpace(item.RequiredSignoffRole)
            ? "Operations reviewer"
            : item.RequiredSignoffRole;
        var signoffStatus = string.IsNullOrWhiteSpace(item.SignoffStatus)
            ? "pending-signoff"
            : item.SignoffStatus;
        var toleranceBand = item.ToleranceBand.HasValue
            ? $" ({item.ToleranceBand.Value.ToString("0.##", CultureInfo.InvariantCulture)} tolerance)"
            : string.Empty;

        return $"Exception route: {exceptionRoute}; tolerance profile {toleranceProfileId}{toleranceBand}; sign-off {signoffStatus} by {requiredSignoffRole}.";
    }

    private static (string Workspace, string TargetRoute, string TargetPageTag) ResolveOperatorNavigation(
        OperatorWorkItemKindDto kind,
        Guid? fundAccountId)
        => kind switch
        {
            OperatorWorkItemKindDto.SecurityMasterCoverage => (
                "Data",
                UiApiRoutes.WorkstationSecurityMasterSearch,
                "DataShell"),
            OperatorWorkItemKindDto.ReconciliationBreak => (
                "Accounting",
                UiApiRoutes.ReconciliationBreakQueue,
                "AccountingShell"),
            OperatorWorkItemKindDto.LedgerPeriodClose => (
                "Accounting",
                UiApiRoutes.ReconciliationBreakQueue,
                "FundReconciliation"),
            OperatorWorkItemKindDto.ReportPackApproval => (
                "Reporting",
                UiApiRoutes.FundReportPacks,
                "ReportingShell"),
            OperatorWorkItemKindDto.BrokerageSync => (
                "Trading",
                fundAccountId.HasValue
                    ? UiApiRoutes.WithParam(UiApiRoutes.FundAccountBrokerageSyncStatus, "accountId", fundAccountId.Value.ToString())
                    : UiApiRoutes.FundAccountBrokerageSyncAccounts,
                "AccountPortfolio"),
            _ => (
                "Trading",
                UiApiRoutes.WorkstationTradingReadiness,
                "TradingShell")
        };

    private static string BuildOperatorInboxSummary(
        IReadOnlyCollection<OperatorWorkItemDto> items,
        int criticalCount,
        int warningCount)
    {
        if (items.Count == 0)
        {
            return "No operator work items are open.";
        }

        if (criticalCount > 0)
        {
            return $"{criticalCount} critical and {warningCount} warning work item(s) need review.";
        }

        if (warningCount > 0)
        {
            return $"{warningCount} warning work item(s) need review.";
        }

        return $"{items.Count} informational work item(s) are available.";
    }

    private static string BuildOperatorInboxScopedId(string prefix, string scope)
    {
        var normalizedPrefix = NormalizeOperatorInboxToken(prefix);
        var normalizedScope = NormalizeOperatorInboxToken(scope);
        return string.IsNullOrEmpty(normalizedScope)
            ? normalizedPrefix
            : $"{normalizedPrefix}-{normalizedScope}";
    }

    private static string NormalizeOperatorInboxToken(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        var previousWasSeparator = false;

        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[length++] = char.ToLowerInvariant(character);
                previousWasSeparator = false;
                continue;
            }

            if (!previousWasSeparator && length > 0)
            {
                buffer[length++] = '-';
                previousWasSeparator = true;
            }
        }

        if (length > 0 && buffer[length - 1] == '-')
        {
            length--;
        }

        return new string(buffer[..length]);
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
                    "Kill-switch can be engaged manually from governance lane."
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
            Readiness: new { },
            Comparisons: Array.Empty<WorkstationModeComparisonGroup>(),
            DrillIn: null);
    }

    private static async Task<WorkstationDataOperationsPayload> BuildDataOperationsPayloadAsync(HttpContext context)
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
            return BuildDataOperationsFallbackPayload(kernelObservability);
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
            trustSnapshots);

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

        return new WorkstationDataOperationsPayload(
            Metrics:
            [
                new WorkstationMetricCard("providers-healthy", "Providers Healthy", healthyProviderCount.ToString(CultureInfo.InvariantCulture), "0", healthyProviderCount > 0 ? "success" : "default"),
                new WorkstationMetricCard("backfills-running", "Backfills Running", activeRuns.ToString(CultureInfo.InvariantCulture), activeRuns == 0 ? "0" : $"+{activeRuns}", activeRuns > 0 ? "default" : "success"),
                new WorkstationMetricCard("exports-ready", "Exports Ready", "0", "0", "default"),
                new WorkstationMetricCard("ops-review", "Needs Review", reviewRuns.ToString(CultureInfo.InvariantCulture), reviewRuns == 0 ? "0" : $"+{reviewRuns}", reviewRuns == 0 ? "default" : "warning"),
                new WorkstationMetricCard("kernel-critical-jumps", "Kernel Jump Alerts", GetKernelActiveAlertCount(kernelObservability).ToString(CultureInfo.InvariantCulture), FormatKernelJumpAlertDelta(kernelObservability), GetKernelJumpAlertTone(kernelObservability))
            ],
            Providers: providers,
            Backfills: backfills,
            Exports: [],
            KernelObservability: BuildKernelObservabilityPayload(kernelObservability));
    }

    private static WorkstationDataOperationsPayload BuildDataOperationsFallbackPayload(KernelObservabilitySnapshot? kernelObservability = null)
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

        return new WorkstationDataOperationsPayload(
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
                new WorkstationDataExportRecord("EX-2196", "python-pandas", "research pack", "Ready", "118k", "7m ago"),
                new WorkstationDataExportRecord("EX-2198", "postgresql", "ops warehouse", "Attention", "42k", "9m ago")
            ],
            KernelObservability: BuildKernelObservabilityPayload(kernelObservability));
    }

    private static WorkstationDataProviderRecord[] BuildWorkstationDataProviderRecords(
        ProviderMetricsStatus? metricsStatus,
        IReadOnlyList<ProviderConnectionRowDto> connectionRows,
        IReadOnlyList<ProviderConnectionDto> routingConnections,
        IReadOnlyList<ProviderBindingDto> routingBindings,
        IReadOnlyList<ProviderTrustSnapshotDto> trustSnapshots)
    {
        var metricLookup = metricsStatus?.Providers.ToDictionary(
            static metric => NormalizeProviderKey(metric.ProviderId),
            static metric => metric,
            StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ProviderMetrics>(StringComparer.OrdinalIgnoreCase);
        var connectionLookup = connectionRows.ToDictionary(
            static connection => NormalizeProviderKey(connection.ProviderId),
            static connection => connection,
            StringComparer.OrdinalIgnoreCase);
        var routingLookup = routingConnections
            .GroupBy(static connection => NormalizeProviderKey(connection.ProviderFamilyId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<ProviderConnectionDto>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var trustLookup = trustSnapshots
            .GroupBy(static snapshot => NormalizeProviderKey(snapshot.ProviderFamilyId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<ProviderTrustSnapshotDto>)group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var bindingLookup = routingBindings
            .GroupBy(static binding => NormalizeProviderKey(binding.ConnectionId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => (IReadOnlyList<ProviderBindingDto>)group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var providerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var providerId in metricLookup.Keys)
        {
            providerIds.Add(providerId);
        }

        foreach (var providerId in connectionLookup.Keys)
        {
            providerIds.Add(providerId);
        }

        foreach (var providerId in routingLookup.Keys)
        {
            providerIds.Add(providerId);
        }

        foreach (var providerId in trustLookup.Keys)
        {
            providerIds.Add(providerId);
        }

        return providerIds
            .Select(providerId =>
            {
                metricLookup.TryGetValue(providerId, out var metrics);
                connectionLookup.TryGetValue(providerId, out var connection);
                routingLookup.TryGetValue(providerId, out var routingConnectionsForProvider);
                trustLookup.TryGetValue(providerId, out var trustSnapshotsForProvider);

                var routingConnection = SelectRepresentativeRoutingConnection(routingConnectionsForProvider);
                var trustSnapshot = SelectRepresentativeTrustSnapshot(trustSnapshotsForProvider, routingConnection?.ConnectionId);
                var bindings = ResolveRoutingBindings(bindingLookup, routingConnectionsForProvider);
                var rationale = metrics is not null
                    ? BuildProviderTrustRationale(metrics)
                    : BuildProviderTrustRationaleFromConnection(connection, routingConnection, trustSnapshot);
                var displayName = ResolveDataProviderDisplayName(providerId, connection, routingConnection, metrics);
                var capability = ResolveDataProviderCapability(connection, routingConnection, metrics);
                var latency = metrics is not null ? $"{metrics.AverageLatencyMs:F0}ms p50" : "Latency not reported";
                var note = BuildDataProviderNote(metrics, connection, trustSnapshot, rationale);

                return new WorkstationDataProviderRecord(
                    ProviderId: connection?.ProviderId ?? routingConnection?.ProviderFamilyId ?? metrics?.ProviderId ?? providerId,
                    DisplayName: displayName,
                    Status: connection is not null ? connection.Health.ToString() : rationale.Status,
                    Capability: capability,
                    Latency: latency,
                    Note: note,
                    TrustScore: trustSnapshot is not null ? FormatScore(NormalizeScore(trustSnapshot.Score)) : rationale.TrustScore,
                    SignalSource: trustSnapshot is not null && trustSnapshot.Signals.Length > 0
                        ? string.Join(", ", trustSnapshot.Signals)
                        : rationale.SignalSource,
                    ReasonCode: rationale.ReasonCode,
                    RecommendedAction: connection?.RecommendedAction ?? rationale.RecommendedAction,
                    GateImpact: rationale.GateImpact,
                    ConnectionSummary: connection,
                    RoutingSummary: new WorkstationDataProviderRoutingSummary(
                        ConnectionId: routingConnection?.ConnectionId,
                        ProviderFamilyId: routingConnection?.ProviderFamilyId ?? connection?.ProviderId,
                        ProductionReady: routingConnection?.ProductionReady,
                        CertificationFresh: trustSnapshot?.IsCertificationFresh,
                        BindingCount: bindings.Count,
                        FallbackRouteCount: bindings.Sum(static binding => binding.FailoverConnectionIds.Length),
                        HealthStatus: trustSnapshot?.HealthStatus ?? connection?.Health.ToString()),
                    Diagnostics: BuildWorkstationProviderDiagnostics(
                        providerId,
                        connection,
                        routingConnection,
                        trustSnapshot,
                        bindings,
                        metrics,
                        rationale));
            })
            .OrderBy(static provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static WorkstationDataProviderRecord BuildFallbackDataProviderRecord(
        string providerId,
        string displayName,
        string status,
        string capability,
        string latency,
        string note,
        string trustScore,
        string signalSource,
        string reasonCode,
        string recommendedAction,
        string gateImpact)
        => new(
            ProviderId: providerId,
            DisplayName: displayName,
            Status: status,
            Capability: capability,
            Latency: latency,
            Note: note,
            TrustScore: trustScore,
            SignalSource: signalSource,
            ReasonCode: reasonCode,
            RecommendedAction: recommendedAction,
            GateImpact: gateImpact,
            ConnectionSummary: null,
            RoutingSummary: new WorkstationDataProviderRoutingSummary(
                ConnectionId: null,
                ProviderFamilyId: providerId,
                ProductionReady: null,
                CertificationFresh: null,
                BindingCount: 0,
                FallbackRouteCount: 0,
                HealthStatus: status),
            Diagnostics:
            [
                new WorkstationDataProviderDiagnostic("provider-health", "Provider health", status == "Healthy" ? "pass" : "warning", status == "Healthy" ? "Pass" : "Review", note),
                new WorkstationDataProviderDiagnostic("trust-state", "Trust state", status == "Healthy" ? "pass" : "warning", trustScore, $"{signalSource}. {recommendedAction}")
            ]);

    private static IReadOnlyList<ProviderBindingDto> ResolveRoutingBindings(
        IReadOnlyDictionary<string, IReadOnlyList<ProviderBindingDto>> bindingLookup,
        IReadOnlyList<ProviderConnectionDto>? routingConnections)
    {
        if (routingConnections is null || routingConnections.Count == 0)
        {
            return [];
        }

        var bindings = new List<ProviderBindingDto>();
        foreach (var routingConnection in routingConnections)
        {
            if (bindingLookup.TryGetValue(NormalizeProviderKey(routingConnection.ConnectionId), out var connectionBindings))
            {
                bindings.AddRange(connectionBindings);
            }
        }

        return bindings
            .DistinctBy(static binding => binding.BindingId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ProviderConnectionDto? SelectRepresentativeRoutingConnection(
        IReadOnlyList<ProviderConnectionDto>? routingConnections)
    {
        if (routingConnections is null || routingConnections.Count == 0)
        {
            return null;
        }

        return routingConnections
            .OrderByDescending(static connection => connection.Enabled)
            .ThenByDescending(static connection => connection.ProductionReady)
            .ThenBy(static connection => connection.ConnectionId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static ProviderTrustSnapshotDto? SelectRepresentativeTrustSnapshot(
        IReadOnlyList<ProviderTrustSnapshotDto>? trustSnapshots,
        string? preferredConnectionId)
    {
        if (trustSnapshots is null || trustSnapshots.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(preferredConnectionId))
        {
            var exactMatch = trustSnapshots.FirstOrDefault(snapshot =>
                snapshot.ConnectionId.Equals(preferredConnectionId, StringComparison.OrdinalIgnoreCase));
            if (exactMatch is not null)
            {
                return exactMatch;
            }
        }

        return trustSnapshots
            .OrderByDescending(static snapshot => snapshot.IsHealthy)
            .ThenByDescending(static snapshot => snapshot.IsProductionReady)
            .ThenByDescending(static snapshot => snapshot.Score)
            .ThenBy(static snapshot => snapshot.ConnectionId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string ResolveDataProviderDisplayName(
        string providerId,
        ProviderConnectionRowDto? connection,
        ProviderConnectionDto? routingConnection,
        ProviderMetrics? metrics)
        => connection?.DisplayName
           ?? routingConnection?.DisplayName
           ?? metrics?.ProviderId
           ?? providerId;

    private static string ResolveDataProviderCapability(
        ProviderConnectionRowDto? connection,
        ProviderConnectionDto? routingConnection,
        ProviderMetrics? metrics)
    {
        if (connection is not null)
        {
            return connection.Capability switch
            {
                ProviderConnectionCapabilityDto.DataAndBrokerage => "Data + Brokerage",
                ProviderConnectionCapabilityDto.Brokerage => "Brokerage",
                _ => "Data"
            };
        }

        return metrics?.ProviderType
            ?? routingConnection?.ConnectionType
            ?? "Provider";
    }

    private static string BuildDataProviderNote(
        ProviderMetrics? metrics,
        ProviderConnectionRowDto? connection,
        ProviderTrustSnapshotDto? trustSnapshot,
        ProviderTrustRationalePayload rationale)
    {
        if (metrics is not null)
        {
            return metrics.IsConnected
                ? $"Active subscriptions: {metrics.ActiveSubscriptions}. Quality score: {rationale.TrustScore}."
                : $"Provider disconnected. Last seen: {metrics.Timestamp:HH:mm} UTC.";
        }

        if (connection?.LastError is { Length: > 0 } error)
        {
            return error;
        }

        if (trustSnapshot is not null && trustSnapshot.Signals.Length > 0)
        {
            return $"Trust signals: {string.Join(", ", trustSnapshot.Signals)}.";
        }

        return rationale.RecommendedAction;
    }

    private static ProviderTrustRationalePayload BuildProviderTrustRationaleFromConnection(
        ProviderConnectionRowDto? connection,
        ProviderConnectionDto? routingConnection,
        ProviderTrustSnapshotDto? trustSnapshot)
    {
        if (connection is null)
        {
            if (trustSnapshot is not null)
            {
                var trustScore = FormatScore(NormalizeScore(trustSnapshot.Score));
                return new ProviderTrustRationalePayload(
                    Status: trustSnapshot.IsHealthy ? "Healthy" : "Warning",
                    TrustScore: trustScore,
                    SignalSource: trustSnapshot.Signals.Length > 0 ? string.Join(", ", trustSnapshot.Signals) : "Provider trust snapshot",
                    ReasonCode: trustSnapshot.IsHealthy ? "TRUST_SNAPSHOT_HEALTHY" : "TRUST_SNAPSHOT_REVIEW",
                    RecommendedAction: trustSnapshot.IsHealthy
                        ? "Provider trust snapshot is healthy."
                        : "Inspect routing trust signals before routing new workflow traffic.",
                    GateImpact: trustSnapshot.IsHealthy ? "Normal operation" : "Health gate needs review");
            }

            return new ProviderTrustRationalePayload(
                Status: "Warning",
                TrustScore: "Not reported",
                SignalSource: "Provider center bootstrap",
                ReasonCode: "PROVIDER_SUMMARY_PENDING",
                RecommendedAction: routingConnection?.Enabled == false
                    ? "Enable the routing connection before selecting this provider."
                    : "Configure provider credentials and routing before relying on this workflow.",
                GateImpact: routingConnection?.Enabled == false ? "Disabled for routing" : "No routing gate loaded");
        }

        return connection.Health switch
        {
            ProviderContinuityHealthDto.Healthy => new ProviderTrustRationalePayload(
                Status: "Healthy",
                TrustScore: trustSnapshot is not null ? FormatScore(NormalizeScore(trustSnapshot.Score)) : "100%",
                SignalSource: trustSnapshot is not null && trustSnapshot.Signals.Length > 0 ? string.Join(", ", trustSnapshot.Signals) : "Provider connection continuity health",
                ReasonCode: "CONNECTION_HEALTHY",
                RecommendedAction: connection.RecommendedAction,
                GateImpact: "Normal operation"),
            ProviderContinuityHealthDto.Degraded => new ProviderTrustRationalePayload(
                Status: "Degraded",
                TrustScore: trustSnapshot is not null ? FormatScore(NormalizeScore(trustSnapshot.Score)) : "70%",
                SignalSource: trustSnapshot is not null && trustSnapshot.Signals.Length > 0 ? string.Join(", ", trustSnapshot.Signals) : "Provider connection continuity health",
                ReasonCode: "CONNECTION_DEGRADED",
                RecommendedAction: connection.RecommendedAction,
                GateImpact: "Degraded"),
            ProviderContinuityHealthDto.Blocked => new ProviderTrustRationalePayload(
                Status: "Blocked",
                TrustScore: trustSnapshot is not null ? FormatScore(NormalizeScore(trustSnapshot.Score)) : "40%",
                SignalSource: trustSnapshot is not null && trustSnapshot.Signals.Length > 0 ? string.Join(", ", trustSnapshot.Signals) : "Provider connection continuity health",
                ReasonCode: "CONNECTION_BLOCKED",
                RecommendedAction: connection.RecommendedAction,
                GateImpact: "Critical"),
            _ => new ProviderTrustRationalePayload(
                Status: "Warning",
                TrustScore: trustSnapshot is not null ? FormatScore(NormalizeScore(trustSnapshot.Score)) : "80%",
                SignalSource: trustSnapshot is not null && trustSnapshot.Signals.Length > 0 ? string.Join(", ", trustSnapshot.Signals) : "Provider connection continuity health",
                ReasonCode: "CONNECTION_REVIEW",
                RecommendedAction: connection.RecommendedAction,
                GateImpact: "Watch")
        };
    }

    private static IReadOnlyList<WorkstationDataProviderDiagnostic> BuildWorkstationProviderDiagnostics(
        string providerId,
        ProviderConnectionRowDto? connection,
        ProviderConnectionDto? routingConnection,
        ProviderTrustSnapshotDto? trustSnapshot,
        IReadOnlyList<ProviderBindingDto> bindings,
        ProviderMetrics? metrics,
        ProviderTrustRationalePayload rationale)
    {
        var diagnostics = new List<WorkstationDataProviderDiagnostic>();
        var hasCredentials = connection is not null &&
            connection.CredentialState is not ProviderCredentialStateDto.Missing and not ProviderCredentialStateDto.Partial;

        diagnostics.Add(new WorkstationDataProviderDiagnostic(
            Id: "credential-presence",
            Label: "Credential presence",
            Status: !hasCredentials ? "warning" : "pass",
            StatusLabel: !hasCredentials ? "Review" : "Pass",
            Detail: connection is null
                ? "No provider credential summary is loaded for this provider."
                : connection.CredentialState switch
                {
                    ProviderCredentialStateDto.NotRequired => "No credentials are required for this provider.",
                    ProviderCredentialStateDto.Missing => "Required credential fields are missing.",
                    ProviderCredentialStateDto.Partial => "Credential setup is incomplete.",
                    ProviderCredentialStateDto.Invalid => "Stored credentials are invalid and must be replaced.",
                    _ => $"Credential state: {connection.CredentialState}."
                }));

        diagnostics.Add(new WorkstationDataProviderDiagnostic(
            Id: "credential-verification",
            Label: "Credential verification",
            Status: connection is null
                ? "pending"
                : connection.VerificationState is ProviderVerificationStateDto.Verified or ProviderVerificationStateDto.NotRequired ? "pass"
                : connection.VerificationState == ProviderVerificationStateDto.Failed ? "fail" : "warning",
            StatusLabel: connection is null
                ? "Pending"
                : connection.VerificationState is ProviderVerificationStateDto.Verified or ProviderVerificationStateDto.NotRequired ? "Pass"
                : connection.VerificationState == ProviderVerificationStateDto.Failed ? "Fail" : "Review",
            Detail: connection?.LastError
                ?? (connection is null
                    ? "Verification requires a provider credential summary."
                    : connection.VerificationState == ProviderVerificationStateDto.Verified
                        ? $"Verified at {FormatProviderTimestamp(connection.LastVerifiedAt)}."
                        : $"Verification state: {connection.VerificationState}.")));

        diagnostics.Add(new WorkstationDataProviderDiagnostic(
            Id: "provider-health",
            Label: "Provider health",
            Status: rationale.Status switch
            {
                "Healthy" => "pass",
                "Blocked" or "Degraded" => "fail",
                _ => "warning"
            },
            StatusLabel: rationale.Status,
            Detail: metrics is not null
                ? $"Latency {metrics.AverageLatencyMs:F0}ms p50; dropped messages {metrics.MessagesDropped}; subscriptions {metrics.ActiveSubscriptions}."
                : rationale.RecommendedAction));

        diagnostics.Add(new WorkstationDataProviderDiagnostic(
            Id: "routing-readiness",
            Label: "Routing readiness",
            Status: routingConnection is null
                ? "pending"
                : !routingConnection.Enabled || !routingConnection.ProductionReady
                    ? "warning"
                    : "pass",
            StatusLabel: routingConnection is null
                ? "Pending"
                : routingConnection.ProductionReady ? "Pass" : "Review",
            Detail: routingConnection is null
                ? "No routing connection is configured for this provider yet."
                : $"Bindings {bindings.Count}; fallback routes {bindings.Sum(static binding => binding.FailoverConnectionIds.Length)}; production ready {routingConnection.ProductionReady}." ));

        diagnostics.Add(new WorkstationDataProviderDiagnostic(
            Id: "trust-state",
            Label: "Trust state",
            Status: trustSnapshot is null
                ? rationale.Status == "Healthy" ? "pass" : "warning"
                : trustSnapshot.IsHealthy ? "pass" : "warning",
            StatusLabel: trustSnapshot?.HealthStatus ?? rationale.TrustScore,
            Detail: trustSnapshot is not null
                ? trustSnapshot.Signals.Length > 0
                    ? string.Join(", ", trustSnapshot.Signals)
                    : "Trust snapshot is available with no active signals."
                : $"{rationale.SignalSource}. {rationale.RecommendedAction}"));

        return diagnostics;
    }

    private static string NormalizeProviderKey(string providerId)
        => providerId.Trim().ToLowerInvariant();

    private static string FormatProviderTimestamp(DateTimeOffset? value)
        => value?.ToString("MMM dd, yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture) ?? "Never";

    private static ProviderTrustRationalePayload BuildProviderTrustRationale(ProviderMetrics metrics)
    {
        var trustScore = NormalizeScore(metrics.DataQualityScore);
        var successRate = NormalizeScore(metrics.ConnectionSuccessRate);
        var gateImpact = BuildProviderGateImpact(trustScore);

        if (!metrics.IsConnected)
        {
            return new ProviderTrustRationalePayload(
                Status: "Degraded",
                TrustScore: FormatScore(trustScore),
                SignalSource: "Provider quote/trade stream health telemetry",
                ReasonCode: "PROVIDER_STREAM_DEGRADED",
                RecommendedAction: "Verify provider connectivity and entitlements, then monitor for recovery before promotion decisions.",
                GateImpact: gateImpact);
        }

        if (metrics.ConnectionFailures > 0 && (metrics.ConnectionAttempts == 0 || successRate < 0.75d))
        {
            return new ProviderTrustRationalePayload(
                Status: "Degraded",
                TrustScore: FormatScore(trustScore),
                SignalSource: "Provider reconnect monitor",
                ReasonCode: "RECONNECT_INSTABILITY",
                RecommendedAction: "Keep run in observation mode; require a stable reconnect window before trusting parity-sensitive outputs.",
                GateImpact: gateImpact);
        }

        if (metrics.MessagesDropped > 0)
        {
            return new ProviderTrustRationalePayload(
                Status: "Degraded",
                TrustScore: FormatScore(trustScore),
                SignalSource: "Missing data completeness checker",
                ReasonCode: "DATA_COMPLETENESS_GAP",
                RecommendedAction: "Trigger targeted backfill or replay and block trust sign-off for impacted symbols or windows.",
                GateImpact: gateImpact);
        }

        if (metrics.AverageLatencyMs >= 250d)
        {
            return new ProviderTrustRationalePayload(
                Status: "Warning",
                TrustScore: FormatScore(trustScore),
                SignalSource: "Latency monitor",
                ReasonCode: "LATENCY_REGRESSION",
                RecommendedAction: "Delay operator promotion actions; review latency trend and compare against baseline window.",
                GateImpact: gateImpact);
        }

        if (trustScore < 0.90d)
        {
            return new ProviderTrustRationalePayload(
                Status: trustScore < 0.80d ? "Degraded" : "Warning",
                TrustScore: FormatScore(trustScore),
                SignalSource: "Cross-provider parity comparator",
                ReasonCode: "PARITY_DRIFT_DETECTED",
                RecommendedAction: "Re-run the parity packet and treat results as non-promotable until drift is explained or corrected.",
                GateImpact: gateImpact);
        }

        return new ProviderTrustRationalePayload(
            Status: "Healthy",
            TrustScore: FormatScore(trustScore),
            SignalSource: "Provider baseline health snapshot",
            ReasonCode: "HEALTHY_BASELINE",
            RecommendedAction: "Continue monitoring provider health; no DK1 action is required.",
            GateImpact: gateImpact);
    }

    private static double NormalizeScore(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0d;
        }

        var normalized = value > 1d ? value / 100d : value;
        return Math.Clamp(normalized, 0d, 1d);
    }

    private static string FormatScore(double score)
        => $"{(score * 100d).ToString("0", CultureInfo.InvariantCulture)}%";

    private static string BuildProviderGateImpact(double trustScore)
        => trustScore >= 0.90d
            ? "Normal operation"
            : trustScore >= 0.80d
                ? "Watch"
                : trustScore >= 0.70d
                    ? "Degraded"
                    : "Critical";

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
            allRuns = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false))
                .Take(12)
                .ToArray();

            // Fetch details for the most recent runs to power the cash-flow summary.
            // Mirrors the Governance workspace pattern; bounded to avoid amplifying load.
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
            CashFlow: BuildGovernanceWorkspaceCashFlowSummary(runDetailsForCashFlow));
    }

    private static async Task<WorkstationGovernancePayload> BuildGovernancePayloadAsync(HttpContext context)
    {
        var readService = context.RequestServices.GetService<StrategyRunReadService>();
        var kernelObservability = context.RequestServices.GetService<KernelObservabilityService>()?.GetSnapshot();
        if (readService is null)
        {
            return BuildGovernanceFallbackPayload(kernelObservability);
        }

        var allRuns = (await readService.GetRunsAsync(ct: context.RequestAborted).ConfigureAwait(false)).ToArray();
        var runs = allRuns.Take(6).ToArray();
        if (runs.Length == 0)
        {
            // PR-03: return typed DTO
            return new WorkstationGovernancePayload(
                Metrics:
                [
                    new WorkstationMetricCard("open-breaks", "Open Breaks", "0", "0%", "success"),
                    new WorkstationMetricCard("timing-drift", "Timing Drift", "0", "0%", "default"),
                    new WorkstationMetricCard("security-gaps", "Security Gaps", "0", "0%", "success"),
                    new WorkstationMetricCard("audit-ready", "Audit Ready", "0", "0%", "default"),
                    new WorkstationMetricCard("kernel-critical-jumps", "Kernel Jump Alerts", GetKernelActiveAlertCount(kernelObservability).ToString(CultureInfo.InvariantCulture), "0%", GetKernelJumpAlertTone(kernelObservability))
                ],
                ReconciliationQueue: Array.Empty<object>(),
                BreakQueue: Array.Empty<object>(),
                Workspace: new WorkstationGovernanceWorkspaceSummary(0, 0, 0, 0, 0),
                CashFlow: BuildGovernanceWorkspaceCashFlowSummary(Array.Empty<StrategyRunDetail?>()),
                Reporting: BuildGovernanceReportingPayload(),
                KernelObservability: BuildKernelObservabilityPayload(kernelObservability));
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

        // PR-03: return typed DTO
        return new WorkstationGovernancePayload(
            Metrics:
            [
                new WorkstationMetricCard("open-breaks", "Open Breaks", openBreaks.ToString(CultureInfo.InvariantCulture), "0%", openBreaks == 0 ? "success" : "warning"),
                new WorkstationMetricCard("timing-drift", "Timing Drift", timingDriftRuns.ToString(CultureInfo.InvariantCulture), "0%", timingDriftRuns == 0 ? "default" : "warning"),
                new WorkstationMetricCard("security-gaps", "Security Gaps", runsWithSecurityIssues.ToString(CultureInfo.InvariantCulture), "0%", runsWithSecurityIssues == 0 ? "success" : "warning"),
                new WorkstationMetricCard("audit-ready", "Audit Ready", Math.Max(0, auditReadyRuns).ToString(CultureInfo.InvariantCulture), "0%", auditReadyRuns > 0 ? "success" : "default"),
                new WorkstationMetricCard("kernel-critical-jumps", "Kernel Jump Alerts", GetKernelActiveAlertCount(kernelObservability).ToString(CultureInfo.InvariantCulture), "0%", GetKernelJumpAlertTone(kernelObservability))
            ],
            ReconciliationQueue: runs
                .Zip(details, static (run, detail) => (run, detail))
                .Zip(reconciliations, (pair, reconciliation) => (object)BuildGovernanceRunCard(pair.run, pair.detail, reconciliation, kernelObservability))
                .ToArray(),
            BreakQueue: (await GetBreakQueueItemsAsync(context.RequestServices, status: null, fundAccountId: null, context.RequestAborted).ConfigureAwait(false))
                .Cast<object>()
                .ToArray(),
            Workspace: new WorkstationGovernanceWorkspaceSummary(
                TotalRuns: allRuns.Length,
                ReconciledRuns: reconciliations.Count(static detail => detail is not null),
                LedgerReadyRuns: runs.Count(static run => !string.IsNullOrWhiteSpace(run.LedgerReference)),
                OpenBreaks: openBreaks,
                SecurityIssues: runsWithSecurityIssues),
            CashFlow: BuildGovernanceWorkspaceCashFlowSummary(details),
            Reporting: BuildGovernanceReportingPayload(),
            KernelObservability: BuildKernelObservabilityPayload(kernelObservability));
    }

    // PR-03: returns typed DTO
    private static WorkstationGovernancePayload BuildGovernanceFallbackPayload(KernelObservabilitySnapshot? kernelObservability = null)
    {
        var metricsCards = new WorkstationMetricCard[]
        {
            new("open-breaks", "Open Breaks", "4", "0%", "warning"),
            new("timing-drift", "Timing Drift", "1", "0%", "warning"),
            new("security-gaps", "Security Gaps", "2", "0%", "warning"),
            new("audit-ready", "Audit Ready", "9", "0%", "success"),
            new("kernel-critical-jumps", "Kernel Jump Alerts", GetKernelActiveAlertCount(kernelObservability).ToString(CultureInfo.InvariantCulture), "0%", GetKernelJumpAlertTone(kernelObservability))
        };
        var reconciliationQueue = new object[]
        {
                new
                {
                    runId = "gov-run-001",
                    strategyName = "Global Macro Overlay",
                    mode = "paper",
                    status = "Completed",
                    lastUpdated = "12m ago",
                    auditReference = "audit-gov-run-001",
                    breakCount = 2,
                    openBreakCount = 2,
                    reconciliationStatus = "BreaksOpen",
                    latestReconciliation = new
                    {
                        breakCount = 2,
                        openBreakCount = 2,
                        hasTimingDrift = false,
                        securityIssueCount = 2,
                        hasSecurityCoverageIssues = true,
                        lastUpdated = "15m ago",
                        tone = "warning"
                    },
                    securityCoverage = new
                    {
                        portfolioResolved = 14,
                        portfolioMissing = 1,
                        ledgerResolved = 12,
                        ledgerMissing = 1,
                        hasIssues = true,
                        tone = "warning",
                        summary = "26 references mapped, 2 unresolved.",
                        resolvedReferences = new[]
                        {
                            new SecurityCoverageReferencePayload(
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
                        },
                        reviewReferences = new[]
                        {
                            new SecurityCoverageReferencePayload(
                                Source: "portfolio",
                                Symbol: "XYZ",
                                AccountName: null,
                                SecurityId: null,
                                DisplayName: "XYZ",
                                AssetClass: null,
                                SubType: null,
                                Currency: null,
                                Status: null,
                                PrimaryIdentifier: "XYZ",
                                CoverageStatus: "Missing",
                                CoverageReason: "Portfolio position is missing a Security Master match.",
                                MatchedIdentifierKind: null,
                                MatchedIdentifierValue: null,
                                MatchedProvider: null),
                            new SecurityCoverageReferencePayload(
                                Source: "ledger",
                                Symbol: "XYZ",
                                AccountName: "Securities",
                                SecurityId: null,
                                DisplayName: "XYZ",
                                AssetClass: null,
                                SubType: null,
                                Currency: null,
                                Status: null,
                                PrimaryIdentifier: "XYZ",
                                CoverageStatus: "Missing",
                                CoverageReason: "Ledger coverage is missing a Security Master match.",
                                MatchedIdentifierKind: null,
                                MatchedIdentifierValue: null,
                                MatchedProvider: null)
                        },
                        missingReferences = new[]
                        {
                            new SecurityCoverageGapPayload(
                                Source: "portfolio",
                                Symbol: "XYZ",
                                AccountName: null,
                                Reason: "Portfolio position is missing a Security Master match."),
                            new SecurityCoverageGapPayload(
                                Source: "ledger",
                                Symbol: "XYZ",
                                AccountName: "Securities",
                                Reason: "Ledger coverage is missing a Security Master match.")
                        }
                    },
                    cashFlow = new
                    {
                        cashBalance = 1_250_000m,
                        ledgerCashBalance = 1_247_500m,
                        cashVariance = -2_500m,
                        financing = 12_500m,
                        realizedPnl = 42_000m,
                        unrealizedPnl = 18_000m,
                        journalEntryCount = 24,
                        tone = "warning",
                        summary = "Cash and ledger balances diverge and should be reviewed."
                    }
                }
        };
        var breakQueue = new object[]
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
                ExceptionRoute: "governance-variance-escalation",
                ToleranceProfileId: "critical-zero-tolerance",
                ToleranceBand: 0m,
                RequiredSignoffRole: "Governance sign-off",
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
                ExceptionRoute: "security-master-governance-review",
                ToleranceProfileId: "coverage-classification-review",
                ToleranceBand: 0m,
                RequiredSignoffRole: "Governance analyst",
                SignoffStatus: "in-review")
        };
        return new WorkstationGovernancePayload(
            Metrics: metricsCards,
            ReconciliationQueue: reconciliationQueue,
            BreakQueue: breakQueue,
            Workspace: new WorkstationGovernanceWorkspaceSummary(12, 9, 10, 4, 2),
            CashFlow: new
            {
                totalCash = 2_450_000m,
                totalLedgerCash = 2_447_500m,
                netVariance = -2_500m,
                totalFinancing = 12_500m,
                runsWithCashSignals = 9,
                runsWithCashVariance = 1,
                tone = "warning",
                summary = "Cash-flow coverage is available for 9 runs; 1 run needs variance review."
            },
            Reporting: BuildGovernanceReportingPayload(),
            KernelObservability: BuildKernelObservabilityPayload(kernelObservability));
    }

    // PR-03: returns typed DTO
    private static WorkstationRunDigest BuildRunDigest(StrategyRunSummary run, StrategyRunDetail? detail)
    {
        return new WorkstationRunDigest(
            RunId: run.RunId,
            StrategyName: run.StrategyName,
            Mode: run.Mode.ToString().ToLowerInvariant(),
            Status: run.Status.ToString(),
            LastUpdated: FormatRelativeTime(run.LastUpdatedAt),
            HasLedger: !string.IsNullOrWhiteSpace(run.LedgerReference),
            HasPortfolio: !string.IsNullOrWhiteSpace(run.PortfolioId),
            SecurityCoverage: BuildSecurityCoverage(detail));
    }

    private static WorkstationPlotToolPayload BuildResearchPlotToolPayload(
        IReadOnlyList<StrategyRunSummary> runs,
        IReadOnlyList<string> selectedRunIds)
    {
        var activeRun = selectedRunIds.Count > 0
            ? runs.FirstOrDefault(run => string.Equals(run.RunId, selectedRunIds[0], StringComparison.OrdinalIgnoreCase)) ?? runs.FirstOrDefault()
            : runs.FirstOrDefault();
        var companionRun = selectedRunIds.Count > 1
            ? runs.FirstOrDefault(run => string.Equals(run.RunId, selectedRunIds[1], StringComparison.OrdinalIgnoreCase))
            : runs.Skip(1).FirstOrDefault();
        var activeStrategy = activeRun?.StrategyName ?? "Meridian PlotTool";
        var companionStrategy = companionRun?.StrategyName;
        var chartTitle = companionStrategy is null ? activeStrategy : $"{activeStrategy} vs {companionStrategy}";
        var queuedCount = runs.Count(static run => run.Status == StrategyRunStatus.Pending);
        var reviewCount = runs.Count(static run =>
            run.Promotion?.RequiresReview == true ||
            run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled);
        var points = BuildPlotToolScatterPoints().ToArray();
        var focusPoint = points.LastOrDefault(static point => point.Emphasis);

        var workspace = new
        {
            eyebrow = "Strategy Lane · PlotTool",
            title = $"{chartTitle} workstation",
            description = "API-backed PlotTool workspace state from workstation strategy payload.",
            statusBadgeLabel = (activeRun?.Mode.ToString() ?? "research").ToUpperInvariant(),
            statusBadgeVariant = ResolveModeVariant(activeRun?.Mode),
            expression = $"{SlugifyForExpression(activeStrategy)}.spread() vs {(companionStrategy is null ? SlugifyForExpression(activeStrategy) : SlugifyForExpression(companionStrategy))}.implied_volatility(3m, forward, 100)",
            toolbarPills = new[] { "MAX", "Daily (MAX)", companionStrategy is null ? "Single study" : "Pair overlay", "0d lag" },
            metaItems = new[]
            {
                activeRun?.DatasetReference ?? activeRun?.FeedReference ?? "Cross-asset sandbox",
                $"{(2184 + (runs.Count * 9)).ToString(CultureInfo.InvariantCulture)} obs",
                "β 0.48",
                "R² 0.71",
                "ρ 0.84"
            },
            xAxisLabel = "Spread (bps)",
            yAxisLabel = "3m implied vol",
            xTicks = new[]
            {
                new { value = 40, label = "20" }, new { value = 120, label = "40" }, new { value = 200, label = "60" },
                new { value = 280, label = "80" }, new { value = 360, label = "100" }, new { value = 440, label = "140" },
                new { value = 520, label = "180" }, new { value = 600, label = "200" }
            },
            yTicks = new[]
            {
                new { value = 44, label = "120" }, new { value = 98, label = "100" }, new { value = 152, label = "80" },
                new { value = 206, label = "60" }, new { value = 260, label = "40" }
            },
            points = points.Select(static point => new { x = point.X, y = point.Y, emphasis = point.Emphasis }).ToArray(),
            studySummary = new[]
            {
                new { id = "primary", label = "Primary notebook", value = activeStrategy },
                new { id = "companion", label = "Pair target", value = companionStrategy ?? "Select a second run" },
                new { id = "queued", label = "Queued studies", value = queuedCount.ToString(CultureInfo.InvariantCulture) },
                new { id = "review", label = "Review queue", value = reviewCount.ToString(CultureInfo.InvariantCulture) }
            },
            legendItems = new[]
            {
                new { id = "history", label = "History", detail = $"{(2184 + (runs.Count * 9)).ToString(CultureInfo.InvariantCulture)} observations", tone = "history" },
                new { id = "current", label = "Current", detail = "88.40 / 73.80", tone = "current" },
                new { id = "trend", label = "OLS fit", detail = "y = 0.48x + 39.31", tone = "trend" },
                new { id = "refresh", label = "Refresh", detail = FormatRelativeTime(activeRun?.LastUpdatedAt ?? DateTimeOffset.UtcNow), tone = "muted" }
            },
            focusPoint = new
            {
                label = "Current marker",
                xValueText = "88.40",
                yValueText = "73.80",
                detail = $"Highlighted at x {focusPoint.X}, y {focusPoint.Y}."
            },
            signalCards = new[]
            {
                new { id = "correlation", label = "Correlation", value = "0.84", detail = "Pearson correlation", tone = "success" },
                new { id = "regression", label = "Regression beta", value = "0.48", detail = "OLS slope", tone = "default" },
                new { id = "queued", label = "Queued studies", value = queuedCount.ToString(CultureInfo.InvariantCulture), detail = "Run-library backlog", tone = queuedCount > 0 ? "warning" : "default" },
                new { id = "review", label = "Review queue", value = reviewCount.ToString(CultureInfo.InvariantCulture), detail = "Promotion review queue", tone = reviewCount > 0 ? "warning" : "success" }
            },
            consoleTitle = "Expression editor",
            consoleBody = companionStrategy is null
                ? "Select a second run to activate pair-study overlays."
                : $"Pair analysis is active for {activeStrategy} versus {companionStrategy}.",
            overlayTitle = "Meridian overlays",
            overlayItems = new[]
            {
                $"Notebook coverage: {runs.Count} retained {(runs.Count == 1 ? "study" : "studies")} in Strategy.",
                $"Queued studies: {queuedCount}.",
                $"Runs requiring review: {reviewCount}."
            }
        };

        var statistics = new
        {
            eyebrow = "Statistics view",
            title = $"{chartTitle} analysis",
            description = $"{activeRun?.DatasetReference ?? activeRun?.FeedReference ?? "Cross-asset sandbox"} · refreshed {FormatRelativeTime(activeRun?.LastUpdatedAt ?? DateTimeOffset.UtcNow)}",
            summaryTiles = new[]
            {
                new { id = "observations", label = "N obs", value = (2184 + (runs.Count * 9)).ToString(CultureInfo.InvariantCulture), detail = "99.7% complete", tone = "default" },
                new { id = "correlation", label = "Correlation", value = "0.84", detail = "Pearson ρ", tone = "success" },
                new { id = "r-squared", label = "R²", value = "0.71", detail = "OLS fit", tone = "success" },
                new { id = "beta", label = "β (slope)", value = "0.48", detail = "SE 0.014", tone = "success" },
                new { id = "sharpe", label = "Sharpe (5d)", value = activeRun is null ? "N/A" : FormatSharpeProxy(activeRun), detail = "Run-linked", tone = "success" }
            },
            distributionBars = new[] { 2, 6, 11, 18, 24, 31, 34, 29, 20, 11, 5, 2 },
            distributionSummary = $"{(2184 + (runs.Count * 9)).ToString(CultureInfo.InvariantCulture)} samples centered on spread 66.84 and IV 71.42.",
            distributionFootnote = $"Latest observation {DateTimeOffset.UtcNow:yyyy-MM-dd} · refreshed {FormatRelativeTime(activeRun?.LastUpdatedAt ?? DateTimeOffset.UtcNow)}.",
            moments = new[]
            {
                new { id = "net-pnl", label = "Net P&L", value = FormatCurrency(activeRun?.NetPnl ?? 0m), benchmark = "Pair summary" },
                new { id = "return", label = "Total return", value = FormatReturn(activeRun?.TotalReturn, activeRun?.NetPnl), benchmark = "Run linked" },
                new { id = "promotion", label = "Promotion state", value = activeRun?.Promotion?.State.ToString() ?? "Unavailable", benchmark = "Strategy posture" }
            },
            regression = new
            {
                equation = "y = 0.48x + 39.31",
                detailItems = new[]
                {
                    $"{chartTitle} remains linked to workstation strategy evidence.",
                    $"Queued studies: {queuedCount}.",
                    $"Review queue: {reviewCount}."
                }
            },
            sampleRows = new[]
            {
                new { id = "sample-1", timestamp = DateTimeOffset.UtcNow.AddMinutes(-2).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), spreadText = "88.40", impliedVolText = "73.80", zScoreText = "1.42", signalText = "Crowded vol", tone = "warning" },
                new { id = "sample-2", timestamp = DateTimeOffset.UtcNow.AddMinutes(-6).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), spreadText = "80.20", impliedVolText = "70.10", zScoreText = "0.82", signalText = "Neutral", tone = "default" }
            }
        };

        var studies = runs.Select(run => (object)new
        {
            id = run.RunId,
            title = run.StrategyName,
            subtitle = $"{run.DatasetReference ?? run.FeedReference ?? "Unassigned"} · {FormatWindow(run.StartedAt, run.CompletedAt)} · {run.Engine}",
            statusText = run.Status.ToString(),
            statusBadgeLabel = run.Mode.ToString().ToUpperInvariant(),
            statusBadgeVariant = ResolveModeVariant(run.Mode),
            metricText = $"{FormatReturn(run.TotalReturn, run.NetPnl)} · Sharpe {FormatSharpeProxy(run)}",
            noteText = BuildRunNotes(run),
            isActive = activeRun is not null && string.Equals(run.RunId, activeRun.RunId, StringComparison.OrdinalIgnoreCase)
        }).ToArray();

        return new WorkstationPlotToolPayload(
            Workspace: workspace,
            Statistics: statistics,
            Studies: studies,
            Tabs:
            [
                new WorkstationPlotToolTabState("workspace", "Workstation", "plottool-workspace-tab", "plottool-workspace-panel", true, "secondary", 0, "Workstation"),
                new WorkstationPlotToolTabState("statistics", "Statistics", "plottool-statistics-tab", "plottool-statistics-panel", false, "ghost", -1, "Statistics")
            ],
            ActiveView: "workspace");
    }

    private static WorkstationPlotToolPayload BuildResearchFallbackPlotToolPayload()
    {
        var fallbackRun = new StrategyRunSummary(
            RunId: "run-research-001",
            StrategyId: "mean-reversion-fx",
            StrategyName: "Mean Reversion FX",
            Mode: StrategyRunMode.Paper,
            Engine: StrategyRunEngine.MeridianNative,
            Status: StrategyRunStatus.Running,
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-20),
            CompletedAt: null,
            DatasetReference: "FX Majors",
            FeedReference: "synthetic:fx",
            PortfolioId: null,
            LedgerReference: null,
            NetPnl: 4200m,
            TotalReturn: 0.042m,
            FinalEquity: 104200m,
            FillCount: 12,
            LastUpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-2),
            AuditReference: null,
            Identity: null,
            Execution: null,
            Promotion: null,
            Governance: null,
            FundProfileId: null,
            FundDisplayName: null,
            ParentRunId: null,
            SweepId: null,
            SweepDefinitionHash: null,
            SweepObjective: null,
            LiveStatus: null,
            PaperStatus: null);

        return BuildResearchPlotToolPayload([fallbackRun], selectedRunIds: Array.Empty<string>());
    }

    private static IEnumerable<(int X, int Y, bool Emphasis)> BuildPlotToolScatterPoints()
    {
        const int startX = 90;
        const int startY = 250;
        for (var index = 0; index < 36; index++)
        {
            var x = startX + (index * 14);
            var y = startY - (index * 5);
            yield return (x, y, index == 35);
        }
    }

    private static string SlugifyForExpression(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "plottool";

        Span<char> buffer = stackalloc char[value.Length];
        var cursor = 0;
        foreach (var character in value.ToLowerInvariant())
        {
            buffer[cursor++] = char.IsLetterOrDigit(character) ? character : '_';
        }

        return new string(buffer[..cursor]).Trim('_');
    }

    private static string ResolveModeVariant(StrategyRunMode? mode)
        => mode switch
        {
            StrategyRunMode.Paper => "paper",
            StrategyRunMode.Live => "live",
            _ => "research"
        };

    private static object BuildResearchRunCard(StrategyRunSummary run, StrategyRunDetail? detail)
    {
        return new
        {
            id = run.RunId,
            strategyName = run.StrategyName,
            engine = run.Engine.ToString(),
            mode = run.Mode.ToString().ToLowerInvariant(),
            status = run.Status.ToString(),
            dataset = run.DatasetReference ?? run.FeedReference ?? "Unassigned",
            window = FormatWindow(run.StartedAt, run.CompletedAt),
            pnl = FormatReturn(run.TotalReturn, run.NetPnl),
            sharpe = FormatSharpeProxy(run),
            lastUpdated = FormatRelativeTime(run.LastUpdatedAt),
            notes = BuildRunNotes(run),
            promotionState = run.Promotion?.State.ToString(),
            ledgerReference = run.LedgerReference,
            portfolioId = run.PortfolioId,
            netPnl = run.NetPnl,
            totalReturn = run.TotalReturn,
            finalEquity = run.FinalEquity,
            securityCoverage = BuildSecurityCoverage(detail),
            drillIn = BuildRunDrillInLinks(run)
        };
    }

    private static InsightFeed BuildBriefingInsightFeed(
        IReadOnlyList<StrategyRunSummary> runs,
        IReadOnlyList<StrategyRunDetail?> details,
        int alertCount)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        if (runs.Count == 0)
        {
            return new InsightFeed(
                FeedId: "research-market-briefing",
                Title: "Pinned Insights",
                Summary: "No saved charts or run insights yet.",
                GeneratedAt: generatedAt,
                Widgets: Array.Empty<InsightWidget>());
        }

        var widgets = runs
            .Zip(details, static (run, detail) => new InsightWidget(
                WidgetId: $"insight-{run.RunId}",
                Title: run.StrategyName,
                Subtitle: $"{run.Mode} · {run.Status}",
                Headline: FormatReturn(run.TotalReturn, run.NetPnl),
                Tone: GetInsightTone(run, detail),
                Summary: BuildInsightSummary(run, detail),
                RunId: run.RunId,
                DrillInRoute: RunRoute(UiApiRoutes.RunsEquityCurve, run.RunId)))
            .Take(3)
            .ToArray();

        return new InsightFeed(
            FeedId: "research-market-briefing",
            Title: "Pinned Insights",
            Summary: $"{runs.Count} tracked run(s) in briefing scope; {alertCount} alert(s) require attention.",
            GeneratedAt: generatedAt,
            Widgets: widgets);
    }

    private static ResearchBriefingRun BuildBriefingRun(StrategyRunSummary run, StrategyRunDetail? detail)
        => new(
            RunId: run.RunId,
            StrategyName: run.StrategyName,
            Mode: run.Mode,
            Status: run.Status,
            Dataset: run.DatasetReference ?? run.FeedReference ?? "Unassigned",
            WindowLabel: FormatWindow(run.StartedAt, run.CompletedAt),
            ReturnLabel: FormatReturn(run.TotalReturn, run.NetPnl),
            SharpeLabel: FormatSharpeProxy(run),
            LastUpdatedLabel: FormatRelativeTime(run.LastUpdatedAt),
            Notes: BuildInsightSummary(run, detail),
            PromotionState: run.Promotion?.State,
            NetPnl: run.NetPnl,
            TotalReturn: run.TotalReturn,
            FinalEquity: run.FinalEquity,
            DrillIn: BuildResearchDrillInLinks(run));

    private static IReadOnlyList<ResearchSavedComparison> BuildSavedComparisons(IReadOnlyList<StrategyRunSummary> runs)
    {
        var groupedComparisons = runs
            .GroupBy(static run => run.StrategyName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var modes = group
                    .OrderBy(static run => run.Mode)
                    .Select(static run => new ResearchSavedComparisonMode(
                        RunId: run.RunId,
                        Mode: run.Mode,
                        Status: run.Status,
                        NetPnl: run.NetPnl,
                        TotalReturn: run.TotalReturn,
                        DrillIn: BuildResearchDrillInLinks(run)))
                    .ToArray();

                return new ResearchSavedComparison(
                    ComparisonId: $"cmp-{group.First().RunId}",
                    StrategyName: group.Key,
                    ModeSummary: string.Join(" -> ", modes.Select(static mode => mode.Mode.ToString())),
                    Summary: BuildComparisonSummary(group.Key, modes),
                    AnchorRunId: modes.FirstOrDefault()?.RunId,
                    Modes: modes);
            })
            .Where(static comparison => comparison.Modes.Count >= 2)
            .Take(4)
            .ToArray();

        if (groupedComparisons.Length > 0)
        {
            return groupedComparisons;
        }

        if (runs.Count < 2)
        {
            return Array.Empty<ResearchSavedComparison>();
        }

        var syntheticModes = runs
            .Take(2)
            .Select(static run => new ResearchSavedComparisonMode(
                RunId: run.RunId,
                Mode: run.Mode,
                Status: run.Status,
                NetPnl: run.NetPnl,
                TotalReturn: run.TotalReturn,
                DrillIn: BuildResearchDrillInLinks(run)))
            .ToArray();

        return
        [
            new ResearchSavedComparison(
                ComparisonId: $"cmp-recent-{syntheticModes[0].RunId}",
                StrategyName: "Recent Runs",
                ModeSummary: string.Join(" vs ", syntheticModes.Select(static mode => mode.Mode.ToString())),
                Summary: "Saved compare lane across the two most recent runs while multi-mode history is still building.",
                AnchorRunId: syntheticModes[0].RunId,
                Modes: syntheticModes)
        ];
    }

    private static IReadOnlyList<ResearchBriefingAlert> BuildBriefingAlerts(
        IReadOnlyList<StrategyRunSummary> runs,
        IReadOnlyList<StrategyRunDetail?> details)
    {
        var alerts = new List<ResearchBriefingAlert>();

        for (var index = 0; index < runs.Count; index++)
        {
            var run = runs[index];
            var detail = index < details.Count ? details[index] : null;
            var coverageIssueCount = GetSecurityCoverageIssueCount(detail);

            if (run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled)
            {
                alerts.Add(new ResearchBriefingAlert(
                    AlertId: $"alert-status-{run.RunId}",
                    Title: $"{run.StrategyName} needs operator review",
                    Summary: $"Run finished with status {run.Status} and should be investigated before it is reused.",
                    Tone: "warning",
                    RunId: run.RunId,
                    ActionLabel: "Open run"));
            }

            if (run.Promotion?.RequiresReview == true)
            {
                alerts.Add(new ResearchBriefingAlert(
                    AlertId: $"alert-promotion-{run.RunId}",
                    Title: $"{run.StrategyName} is queued for promotion review",
                    Summary: run.Promotion.Reason,
                    Tone: "default",
                    RunId: run.RunId,
                    ActionLabel: "Review promotion"));
            }

            if (coverageIssueCount > 0)
            {
                alerts.Add(new ResearchBriefingAlert(
                    AlertId: $"alert-security-{run.RunId}",
                    Title: $"{run.StrategyName} has Security Master gaps",
                    Summary: $"{coverageIssueCount} unresolved portfolio or ledger reference(s) should be fixed before handoff.",
                    Tone: "warning",
                    RunId: run.RunId,
                    ActionLabel: "Inspect continuity"));
            }
        }

        if (alerts.Count == 0)
        {
            return
            [
                new ResearchBriefingAlert(
                    AlertId: "alert-none",
                    Title: "No blocking alerts",
                    Summary: "Recent runs have no failed states, open promotion blockers, or Security Master gaps.",
                    Tone: "success",
                    ActionLabel: "Browse runs")
            ];
        }

        return alerts
            .Take(4)
            .ToArray();
    }

    private static IReadOnlyList<ResearchWhatChangedItem> BuildWhatChangedItems(IReadOnlyList<StrategyRunSummary> runs)
        => runs
            .Take(4)
            .Select(static run => new ResearchWhatChangedItem(
                ChangeId: $"change-{run.RunId}",
                Title: $"{run.StrategyName} moved to {run.Mode}",
                Summary: BuildChangeSummary(run),
                Category: run.Mode.ToString().ToLowerInvariant(),
                Timestamp: run.LastUpdatedAt,
                RelativeTime: FormatRelativeTime(run.LastUpdatedAt),
                RunId: run.RunId))
            .ToArray();

    private static string BuildInsightSummary(StrategyRunSummary run, StrategyRunDetail? detail)
    {
        var coverageIssueCount = GetSecurityCoverageIssueCount(detail);
        if (coverageIssueCount > 0)
        {
            return $"{BuildRunNotes(run)} {coverageIssueCount} Security Master gap(s) remain open.";
        }

        return BuildRunNotes(run);
    }

    private static string BuildComparisonSummary(
        string strategyName,
        IReadOnlyList<ResearchSavedComparisonMode> modes)
    {
        if (modes.Count == 0)
        {
            return $"No comparison history saved for {strategyName}.";
        }

        if (modes.Count == 1)
        {
            return $"Baseline comparison package is ready for {strategyName}.";
        }

        return $"Saved compare lane covers {modes.Count} lifecycle stage(s) for {strategyName}.";
    }

    private static string BuildChangeSummary(StrategyRunSummary run)
        => run.Status switch
        {
            StrategyRunStatus.Running => $"{run.StrategyName} is still running with updated execution and workspace telemetry.",
            StrategyRunStatus.Completed when run.Promotion?.RequiresReview == true => $"{run.StrategyName} completed and is ready for promotion review.",
            StrategyRunStatus.Completed => $"{run.StrategyName} completed and remains available for compare and pin workflows.",
            StrategyRunStatus.Failed => $"{run.StrategyName} failed and should be reviewed before promotion or reuse.",
            StrategyRunStatus.Cancelled or StrategyRunStatus.Stopped => $"{run.StrategyName} stopped before promotion and is retained for evidence.",
            _ => BuildRunNotes(run)
        };

    private static string GetInsightTone(StrategyRunSummary run, StrategyRunDetail? detail)
    {
        if (run.Status is StrategyRunStatus.Failed or StrategyRunStatus.Cancelled)
        {
            return "warning";
        }

        if (run.Promotion?.RequiresReview == true || GetSecurityCoverageIssueCount(detail) > 0)
        {
            return "default";
        }

        return (run.NetPnl ?? 0m) >= 0m ? "success" : "warning";
    }

    private static int GetSecurityCoverageIssueCount(StrategyRunDetail? detail)
        => (detail?.Portfolio?.SecurityMissingCount ?? 0) + (detail?.Ledger?.SecurityMissingCount ?? 0);

    private static ResearchRunDrillInLinks BuildResearchDrillInLinks(StrategyRunSummary run)
        => new(
            EquityCurve: RunRoute(UiApiRoutes.RunsEquityCurve, run.RunId),
            Fills: RunRoute(UiApiRoutes.RunsFills, run.RunId),
            Attribution: RunRoute(UiApiRoutes.RunsAttribution, run.RunId),
            Ledger: string.IsNullOrWhiteSpace(run.LedgerReference) ? null : RunRoute(UiApiRoutes.RunsLedger, run.RunId),
            CashFlows: RunRoute(UiApiRoutes.PortfolioCashFlows, run.RunId),
            Continuity: RunRoute(UiApiRoutes.RunsContinuity, run.RunId));

    // PR-03: returns typed DTO
    private static WorkstationTimelineCard BuildTimelineCard(StrategyRunSummary run) =>
        new WorkstationTimelineCard(
            RunId: run.RunId,
            StrategyName: run.StrategyName,
            Mode: run.Mode.ToString().ToLowerInvariant(),
            Status: run.Status.ToString(),
            StartedAt: run.StartedAt,
            CompletedAt: run.CompletedAt,
            LastUpdatedAt: run.LastUpdatedAt,
            TotalReturn: run.TotalReturn);

    // PR-03: returns typed comparison groups
    private static IReadOnlyList<WorkstationModeComparisonGroup> BuildModeComparisons(IReadOnlyList<StrategyRunSummary> runs)
    {
        return runs
            .GroupBy(static run => run.StrategyName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var modes = group
                    .OrderBy(static run => run.Mode)
                    .Select(static run => (object)new
                    {
                        runId = run.RunId,
                        mode = run.Mode.ToString().ToLowerInvariant(),
                        status = run.Status.ToString(),
                        netPnl = run.NetPnl,
                        totalReturn = run.TotalReturn,
                        drillIn = BuildRunDrillInLinks(run)
                    })
                    .ToArray();
                return new WorkstationModeComparisonGroup(group.Key, modes);
            })
            .Where(static group => group.Modes.Count > 0)
            .ToArray();
    }

    private static object BuildRunDrillInLinks(StrategyRunSummary run) => new
    {
        equityCurve = RunRoute(UiApiRoutes.RunsEquityCurve, run.RunId),
        fills = RunRoute(UiApiRoutes.RunsFills, run.RunId),
        attribution = RunRoute(UiApiRoutes.RunsAttribution, run.RunId),
        ledger = string.IsNullOrWhiteSpace(run.LedgerReference) ? null : RunRoute(UiApiRoutes.RunsLedger, run.RunId),
        cashFlows = RunRoute(UiApiRoutes.PortfolioCashFlows, run.RunId),
        continuity = RunRoute(UiApiRoutes.RunsContinuity, run.RunId),
        comparison = UiApiRoutes.RunsCompare
    };

    private static string RunRoute(string routeTemplate, string runId)
        => UiApiRoutes.WithParam(routeTemplate, "runId", runId);

    private static IReadOnlyList<StrategyRunMode>? ParseModes(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return null;
        }

        var parsed = mode
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static token => Enum.TryParse<StrategyRunMode>(token, true, out var modeValue)
                ? (StrategyRunMode?)modeValue
                : null)
            .Where(static item => item.HasValue)
            .Select(static item => item!.Value)
            .Distinct()
            .ToArray();

        return parsed.Length == 0 ? null : parsed;
    }

    private static IReadOnlyList<StrategyRunMode>? ParseModes(IReadOnlyList<string>? modes)
    {
        if (modes is not { Count: > 0 })
        {
            return null;
        }

        var parsed = modes
            .Select(static token => Enum.TryParse<StrategyRunMode>(token, true, out var modeValue)
                ? (StrategyRunMode?)modeValue
                : null)
            .Where(static item => item.HasValue)
            .Select(static item => item!.Value)
            .Distinct()
            .ToArray();

        return parsed.Length == 0 ? null : parsed;
    }

    private static object BuildGovernanceRunCard(
        StrategyRunSummary run,
        StrategyRunDetail? detail,
        ReconciliationRunDetail? reconciliation,
        KernelObservabilitySnapshot? kernelObservability)
    {
        return new
        {
            runId = run.RunId,
            strategyName = run.StrategyName,
            mode = run.Mode.ToString().ToLowerInvariant(),
            status = run.Status.ToString(),
            lastUpdated = FormatRelativeTime(run.LastUpdatedAt),
            auditReference = run.AuditReference,
            ledgerReference = run.LedgerReference,
            portfolioId = run.PortfolioId,
            breakCount = reconciliation?.Summary.BreakCount ?? 0,
            openBreakCount = reconciliation?.Summary.OpenBreakCount ?? 0,
            reconciliationStatus = MapReconciliationStatus(reconciliation),
            governance = new
            {
                hasAuditTrail = run.Governance?.HasAuditTrail ?? false,
                hasPortfolio = run.Governance?.HasPortfolio ?? false,
                hasLedger = run.Governance?.HasLedger ?? false,
                datasetReference = run.Governance?.DatasetReference,
                feedReference = run.Governance?.FeedReference
            },
            securityCoverage = BuildSecurityCoverage(detail),
            cashFlow = BuildGovernanceRunCashFlowSummary(detail),
            latestReconciliation = reconciliation is null
                ? null
                : new
                {
                    reconciliationRunId = reconciliation.Summary.ReconciliationRunId,
                    breakCount = reconciliation.Summary.BreakCount,
                    openBreakCount = reconciliation.Summary.OpenBreakCount,
                    matchCount = reconciliation.Summary.MatchCount,
                    hasTimingDrift = reconciliation.Summary.HasTimingDrift,
                    securityIssueCount = reconciliation.Summary.SecurityIssueCount,
                    hasSecurityCoverageIssues = reconciliation.Summary.HasSecurityCoverageIssues,
                    lastUpdated = FormatRelativeTime(reconciliation.Summary.CreatedAt),
                    tone = reconciliation.Summary.BreakCount == 0 && !reconciliation.Summary.HasSecurityCoverageIssues ? "success" : "warning"
                },
            kernelObservability = BuildKernelObservabilityPayload(kernelObservability)
        };
    }

    private static int GetKernelActiveAlertCount(KernelObservabilitySnapshot? snapshot)
        => snapshot?.ActiveAlertCount ?? 0;

    private static int GetKernelTotalAlertCount(KernelObservabilitySnapshot? snapshot)
        => snapshot?.AlertCount ?? 0;

    private static string GetKernelJumpAlertTone(KernelObservabilitySnapshot? snapshot)
        => GetKernelActiveAlertCount(snapshot) == 0 ? "success" : "warning";

    private static string FormatKernelJumpAlertDelta(KernelObservabilitySnapshot? snapshot)
        => $"{GetKernelTotalAlertCount(snapshot).ToString(CultureInfo.InvariantCulture)} total";

    private static object BuildKernelObservabilityPayload(KernelObservabilitySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new
            {
                updatedAtUtc = (DateTimeOffset?)null,
                determinismChecksEnabled = false,
                activeAlerts = 0,
                totalAlerts = 0,
                alerts = 0,
                domains = Array.Empty<object>()
            };
        }

        return new
        {
            updatedAtUtc = snapshot.UpdatedAtUtc,
            determinismChecksEnabled = snapshot.DeterminismChecksEnabled,
            activeAlerts = snapshot.ActiveAlertCount,
            totalAlerts = snapshot.AlertCount,
            alerts = snapshot.AlertCount,
            domains = snapshot.Domains.Select(static domain => new
            {
                domain = domain.Domain,
                evaluations = domain.Evaluations,
                throughputPerMinute = domain.ThroughputPerMinute,
                latencyMs = new
                {
                    p50 = domain.Latency.P50Ms,
                    p95 = domain.Latency.P95Ms,
                    p99 = domain.Latency.P99Ms
                },
                reasonCoveragePercent = domain.ReasonCodeCoveragePercent,
                drift = new
                {
                    score = domain.ScoreDrift,
                    severity = domain.SeverityDrift,
                    methodology = "totalVariationDistance"
                },
                criticalSeverityRate = new
                {
                    shortWindow = domain.CriticalRateShortWindow,
                    longWindow = domain.CriticalRateLongWindow,
                    shortWindowSamples = domain.CriticalRateShortWindowSamples,
                    longWindowSamples = domain.CriticalRateLongWindowSamples,
                    jumpAlertActive = domain.CriticalJumpActive,
                    jumpAlertCount = domain.CriticalJumpAlertCount,
                    alertThresholds = new
                    {
                        minimumSampleCount = domain.CriticalJumpThresholds.MinimumSampleCount,
                        minimumShortRate = domain.CriticalJumpThresholds.MinimumShortRate,
                        zeroBaselineShortRate = domain.CriticalJumpThresholds.ZeroBaselineShortRate,
                        relativeMultiplier = domain.CriticalJumpThresholds.RelativeMultiplier,
                        absoluteIncrease = domain.CriticalJumpThresholds.AbsoluteIncrease
                    }
                },
                determinismMismatches = domain.DeterminismMismatches,
                lastUpdatedUtc = domain.LastUpdatedUtc
            })
        };
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

    private static object BuildSecurityCoverage(StrategyRunDetail? detail)
    {
        var portfolio = detail?.Portfolio;
        var ledger = detail?.Ledger;
        var portfolioResolved = portfolio?.SecurityResolvedCount ?? 0;
        var portfolioMissing = portfolio?.SecurityMissingCount ?? 0;
        var ledgerResolved = ledger?.SecurityResolvedCount ?? 0;
        var ledgerMissing = ledger?.SecurityMissingCount ?? 0;
        var hasIssues = portfolioMissing > 0 || ledgerMissing > 0;
        var resolvedReferences = BuildResolvedSecurityReferences(detail);
        var missingReferences = BuildMissingSecurityReferences(detail);
        var resolvedCount = portfolioResolved + ledgerResolved;
        var missingCount = portfolioMissing + ledgerMissing;

        return new
        {
            portfolioResolved,
            portfolioMissing,
            ledgerResolved,
            ledgerMissing,
            hasIssues,
            tone = hasIssues ? "warning" : resolvedCount > 0 ? "success" : "default",
            summary = missingCount > 0
                ? $"{resolvedCount} references mapped, {missingCount} unresolved."
                : resolvedCount > 0
                    ? $"{resolvedCount} references mapped with no unresolved symbols."
                    : "Security Master coverage not yet evaluated.",
            resolvedReferences,
            missingReferences
        };
    }

    private static SecurityCoverageReferencePayload[] BuildResolvedSecurityReferences(StrategyRunDetail? detail)
    {
        if (detail is null)
        {
            return [];
        }

        var results = new List<SecurityCoverageReferencePayload>();

        if (detail.Portfolio is not null)
        {
            results.AddRange(
                detail.Portfolio.Positions
                    .Where(static position => position.Security is not null)
                    .Select(static position => new SecurityCoverageReferencePayload(
                        Source: "portfolio",
                        Symbol: position.Symbol,
                        AccountName: null,
                        SecurityId: position.Security!.SecurityId.ToString("N"),
                        DisplayName: position.Security.DisplayName,
                        AssetClass: position.Security.AssetClass,
                        SubType: position.Security.SubType,
                        Currency: position.Security.Currency,
                        Status: position.Security.Status.ToString(),
                        PrimaryIdentifier: position.Security.PrimaryIdentifier,
                        CoverageStatus: position.Security.CoverageStatus.ToString(),
                        CoverageReason: position.Security.ResolutionReason,
                        MatchedIdentifierKind: position.Security.MatchedIdentifierKind,
                        MatchedIdentifierValue: position.Security.MatchedIdentifierValue,
                        MatchedProvider: position.Security.MatchedProvider)));
        }

        if (detail.Ledger is not null)
        {
            results.AddRange(
                detail.Ledger.TrialBalance
                    .Where(static line => line.Security is not null && !string.IsNullOrWhiteSpace(line.Symbol))
                    .Select(static line => new SecurityCoverageReferencePayload(
                        Source: "ledger",
                        Symbol: line.Symbol!,
                        AccountName: line.AccountName,
                        SecurityId: line.Security!.SecurityId.ToString("N"),
                        DisplayName: line.Security.DisplayName,
                        AssetClass: line.Security.AssetClass,
                        SubType: line.Security.SubType,
                        Currency: line.Security.Currency,
                        Status: line.Security.Status.ToString(),
                        PrimaryIdentifier: line.Security.PrimaryIdentifier,
                        CoverageStatus: line.Security.CoverageStatus.ToString(),
                        CoverageReason: line.Security.ResolutionReason,
                        MatchedIdentifierKind: line.Security.MatchedIdentifierKind,
                        MatchedIdentifierValue: line.Security.MatchedIdentifierValue,
                        MatchedProvider: line.Security.MatchedProvider)));
        }

        return results
            .DistinctBy(static item => $"{item.Source}|{item.Symbol}|{item.AccountName}|{item.SecurityId}", StringComparer.OrdinalIgnoreCase)
            .Take(SecurityCoveragePreviewLimit)
            .ToArray();
    }

    private static SecurityCoverageGapPayload[] BuildMissingSecurityReferences(StrategyRunDetail? detail)
    {
        if (detail is null)
        {
            return [];
        }

        var results = new List<SecurityCoverageGapPayload>();

        if (detail.Portfolio is not null)
        {
            results.AddRange(
                detail.Portfolio.Positions
                    .Where(static position => position.Security is null && !string.IsNullOrWhiteSpace(position.Symbol))
                    .Select(static position => new SecurityCoverageGapPayload(
                        Source: "portfolio",
                        Symbol: position.Symbol,
                        AccountName: null,
                        Reason: "Portfolio position is missing a Security Master match.")));
        }

        if (detail.Ledger is not null)
        {
            results.AddRange(
                detail.Ledger.TrialBalance
                    .Where(static line => line.Security is null && !string.IsNullOrWhiteSpace(line.Symbol))
                    .Select(static line => new SecurityCoverageGapPayload(
                        Source: "ledger",
                        Symbol: line.Symbol!,
                        AccountName: line.AccountName,
                        Reason: "Ledger coverage is missing a Security Master match.")));
        }

        return results
            .DistinctBy(static item => $"{item.Source}|{item.Symbol}|{item.AccountName}", StringComparer.OrdinalIgnoreCase)
            .Take(SecurityCoveragePreviewLimit)
            .ToArray();
    }

    private static Dictionary<string, WorkstationSecurityReference?> BuildPositionSecurityLookup(StrategyRunDetail? detail)
        => detail?.Portfolio?.Positions
            .Where(static position => !string.IsNullOrWhiteSpace(position.Symbol))
            .GroupBy(static position => position.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First().Security, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, WorkstationSecurityReference?>(StringComparer.OrdinalIgnoreCase);

    private static object BuildTradingPositionPayload(
        string symbol,
        string side,
        string quantity,
        string averagePrice,
        string markPrice,
        string dayPnl,
        string unrealizedPnl,
        string exposure,
        WorkstationSecurityReference? security)
        => new
        {
            symbol,
            side,
            quantity,
            averagePrice,
            markPrice,
            dayPnl,
            unrealizedPnl,
            exposure,
            security = BuildInlineSecurityReference(symbol, security)
        };

    private static object? BuildInlineSecurityReference(string symbol, WorkstationSecurityReference? security)
    {
        if (security is null)
        {
            return null;
        }

        return new
        {
            securityId = security.SecurityId == Guid.Empty ? null : security.SecurityId.ToString("N"),
            displayName = string.IsNullOrWhiteSpace(security.DisplayName) ? symbol : security.DisplayName,
            assetClass = string.IsNullOrWhiteSpace(security.AssetClass) ? null : security.AssetClass,
            subType = security.SubType,
            currency = string.IsNullOrWhiteSpace(security.Currency) ? null : security.Currency,
            status = security.Status.ToString(),
            primaryIdentifier = security.PrimaryIdentifier,
            coverageStatus = security.CoverageStatus.ToString(),
            matchedIdentifierKind = security.MatchedIdentifierKind,
            matchedIdentifierValue = security.MatchedIdentifierValue,
            matchedProvider = security.MatchedProvider,
            resolutionReason = security.ResolutionReason
        };
    }

    private static SecurityCoverageReferencePayload BuildSecurityCoverageReference(
        string Source,
        string Symbol,
        string? AccountName,
        WorkstationSecurityReference? Security)
        => new(
            Source: Source,
            Symbol: Symbol,
            AccountName: AccountName,
            SecurityId: Security is null || Security.SecurityId == Guid.Empty ? null : Security.SecurityId.ToString("N"),
            DisplayName: string.IsNullOrWhiteSpace(Security?.DisplayName) ? Symbol : Security!.DisplayName,
            AssetClass: string.IsNullOrWhiteSpace(Security?.AssetClass) ? null : Security!.AssetClass,
            SubType: Security?.SubType,
            Currency: string.IsNullOrWhiteSpace(Security?.Currency) ? null : Security!.Currency,
            Status: Security?.Status.ToString(),
            PrimaryIdentifier: Security?.PrimaryIdentifier,
            CoverageStatus: Security?.CoverageStatus.ToString() ?? WorkstationSecurityCoverageStatus.Missing.ToString(),
            CoverageReason: BuildSecurityCoverageReason(Source, AccountName, Security),
            MatchedIdentifierKind: Security?.MatchedIdentifierKind,
            MatchedIdentifierValue: Security?.MatchedIdentifierValue,
            MatchedProvider: Security?.MatchedProvider);

    private static string? BuildSecurityCoverageReason(
        string source,
        string? accountName,
        WorkstationSecurityReference? security)
    {
        if (!string.IsNullOrWhiteSpace(security?.ResolutionReason))
        {
            return security.ResolutionReason;
        }

        return security?.CoverageStatus switch
        {
            WorkstationSecurityCoverageStatus.Resolved => null,
            WorkstationSecurityCoverageStatus.Partial => "Security Master coverage is partial and requires operator review.",
            WorkstationSecurityCoverageStatus.Unavailable => "Security Master is unavailable in this environment.",
            _ when string.Equals(source, "ledger", StringComparison.OrdinalIgnoreCase)
                => string.IsNullOrWhiteSpace(accountName)
                    ? "Ledger coverage is missing a Security Master match."
                    : $"Ledger coverage in '{accountName}' is missing a Security Master match.",
            _ => "Portfolio position is missing a Security Master match."
        };
    }

    private static bool HasAuthoritativeSecurityMatch(WorkstationSecurityReference? security)
        => security is not null &&
           security.SecurityId != Guid.Empty &&
           security.CoverageStatus is WorkstationSecurityCoverageStatus.Resolved
               or WorkstationSecurityCoverageStatus.Partial;

    private static bool NeedsSecurityReview(WorkstationSecurityReference? security)
        => security is null ||
           security.CoverageStatus is WorkstationSecurityCoverageStatus.Partial
                or WorkstationSecurityCoverageStatus.Missing
                or WorkstationSecurityCoverageStatus.Unavailable;
    private static object BuildGovernanceWorkspaceCashFlowSummary(IReadOnlyList<StrategyRunDetail?> details)
    {
        var totalCash = details.Sum(static detail => detail?.Portfolio?.Cash ?? 0m);
        var totalLedgerCash = details.Sum(static detail => GetLedgerCashBalance(detail?.Ledger) ?? 0m);
        var totalFinancing = details.Sum(static detail => detail?.Portfolio?.Financing ?? 0m);
        var runsWithCashSignals = details.Count(static detail => detail?.Portfolio is not null || detail?.Ledger is not null);
        var runsWithCashVariance = details.Count(static detail => Math.Abs(GetCashVariance(detail)) > 0.01m);
        var netVariance = totalLedgerCash - totalCash;

        return new
        {
            totalCash,
            totalLedgerCash,
            netVariance,
            totalFinancing,
            runsWithCashSignals,
            runsWithCashVariance,
            tone = runsWithCashVariance > 0 ? "warning" : runsWithCashSignals > 0 ? "success" : "default",
            summary = runsWithCashSignals == 0
                ? "Cash-flow coverage is not yet available."
                : runsWithCashVariance > 0
                    ? $"Cash-flow coverage is available for {runsWithCashSignals} runs; {runsWithCashVariance} run needs variance review."
                    : $"Cash-flow coverage is aligned across {runsWithCashSignals} runs."
        };
    }

    private static object BuildGovernanceRunCashFlowSummary(StrategyRunDetail? detail)
    {
        var cashBalance = detail?.Portfolio?.Cash ?? 0m;
        var ledgerCashBalance = GetLedgerCashBalance(detail?.Ledger) ?? 0m;
        var cashVariance = ledgerCashBalance - cashBalance;
        var financing = detail?.Portfolio?.Financing ?? 0m;
        var realizedPnl = detail?.Portfolio?.RealizedPnl ?? 0m;
        var unrealizedPnl = detail?.Portfolio?.UnrealizedPnl ?? 0m;
        var journalEntryCount = detail?.Ledger?.JournalEntryCount ?? 0;
        var hasSignals = detail?.Portfolio is not null || detail?.Ledger is not null;

        return new
        {
            cashBalance,
            ledgerCashBalance,
            cashVariance,
            financing,
            realizedPnl,
            unrealizedPnl,
            journalEntryCount,
            tone = !hasSignals ? "default" : Math.Abs(cashVariance) > 0.01m ? "warning" : "success",
            summary = !hasSignals
                ? "Cash-flow coverage is not yet available."
                : Math.Abs(cashVariance) > 0.01m
                    ? "Cash and ledger balances diverge and should be reviewed."
                    : "Cash and ledger balances are aligned."
        };
    }

    private static decimal? GetLedgerCashBalance(LedgerSummary? ledger)
        => ledger?.TrialBalance.FirstOrDefault(static line =>
            string.Equals(line.AccountName, "Cash", StringComparison.OrdinalIgnoreCase))?.Balance;

    private static decimal GetCashVariance(StrategyRunDetail? detail)
    {
        var portfolioCash = detail?.Portfolio?.Cash;
        var ledgerCash = GetLedgerCashBalance(detail?.Ledger);
        if (!portfolioCash.HasValue || !ledgerCash.HasValue)
        {
            return 0m;
        }

        return ledgerCash.Value - portfolioCash.Value;
    }

    private static WorkstationGovernanceReportingPayload BuildGovernanceReportingPayload()
    {
        var profiles = ExportProfile.GetBuiltInProfiles()
            .Select(static profile => new WorkstationGovernanceReportingProfilePayload(
                Id: profile.Id,
                Name: profile.Name,
                TargetTool: profile.TargetTool,
                Format: profile.Format.ToString(),
                Description: profile.Description ?? string.Empty,
                LoaderScript: profile.IncludeLoaderScript,
                DataDictionary: profile.IncludeDataDictionary))
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var recommended = profiles
            .Where(static profile => profile.Id is "excel" or "python-pandas" or "postgresql" or "arrow-feather")
            .Select(static profile => profile.Id)
            .ToArray();

        return new WorkstationGovernanceReportingPayload(
            ProfileCount: profiles.Length,
            RecommendedProfiles: recommended,
            Profiles: profiles,
            ReportPackTargets: ["board", "investor", "compliance", "fund-ops"],
            Summary: $"{profiles.Length} export/reporting profiles are available for governance workflows.");
    }

    private static string BuildDisplayName(StrategyRunSummary? latest)
        => latest is null ? "Meridian Operator" : $"{latest.StrategyName} Desk";

    private static string BuildRole(StrategyRunSummary? latest)
        => latest is null
            ? "Research Lead"
            : latest.Mode == StrategyRunMode.Live
                ? "Live Operations"
                : "Research Lead";

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
                _ => "Run is flagged for governance review."
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

    private static SecurityMasterWorkstationDto MapToWorkstationSecurity(SecuritySummaryDto summary)
        => new(
            SecurityId: summary.SecurityId,
            DisplayName: summary.DisplayName,
            Status: summary.Status,
            Classification: new SecurityClassificationSummaryDto(
                AssetClass: summary.AssetClass,
                SubType: DeriveSubType(summary.AssetClass),
                PrimaryIdentifierKind: null,
                PrimaryIdentifierValue: summary.PrimaryIdentifier,
                MatchedIdentifierKind: null,
                MatchedIdentifierValue: null,
                MatchedProvider: null),
            EconomicDefinition: new SecurityEconomicDefinitionSummaryDto(
                Currency: summary.Currency,
                Version: summary.Version,
                EffectiveFrom: null,
                EffectiveTo: null));

    private static SecurityMasterWorkstationDto MapToWorkstationSecurity(SecurityDetailDto detail)
    {
        var primaryIdentifier = detail.Identifiers
            .FirstOrDefault(static identifier => identifier.IsPrimary)
            ?? detail.Identifiers.FirstOrDefault();

        return new SecurityMasterWorkstationDto(
            SecurityId: detail.SecurityId,
            DisplayName: detail.DisplayName,
            Status: detail.Status,
            Classification: new SecurityClassificationSummaryDto(
                AssetClass: detail.AssetClass,
                SubType: DeriveSubType(detail.AssetClass),
                PrimaryIdentifierKind: primaryIdentifier?.Kind.ToString(),
                PrimaryIdentifierValue: primaryIdentifier?.Value,
                MatchedIdentifierKind: null,
                MatchedIdentifierValue: null,
                MatchedProvider: null),
            EconomicDefinition: new SecurityEconomicDefinitionSummaryDto(
                Currency: detail.Currency,
                Version: detail.Version,
                EffectiveFrom: detail.EffectiveFrom,
                EffectiveTo: detail.EffectiveTo));
    }

    private static SecurityIdentityDrillInDto MapToIdentityDrillIn(SecurityDetailDto detail)
        => new(
            SecurityId: detail.SecurityId,
            DisplayName: detail.DisplayName,
            AssetClass: detail.AssetClass,
            Status: detail.Status,
            Version: detail.Version,
            EffectiveFrom: detail.EffectiveFrom,
            EffectiveTo: detail.EffectiveTo,
            Identifiers: detail.Identifiers,
            Aliases: detail.Aliases);

    private static SecurityEconomicDefinitionSummaryDto MapToEconomicDefinitionSummary(SecurityEconomicDefinitionRecord record)
        => new(
            Currency: record.Currency,
            Version: record.Version,
            EffectiveFrom: record.EffectiveFrom,
            EffectiveTo: record.EffectiveTo,
            SubType: record.SubType,
            AssetFamily: record.AssetFamily,
            IssuerType: record.IssuerType);

    /// <summary>
    /// Derives the most specific sub-type available from the asset-class string without requiring
    /// a full aggregate rebuild. Returns null for asset classes that may map to multiple sub-types.
    /// </summary>
    private static string? DeriveSubType(string? assetClass) => assetClass switch
    {
        "Bond" => "Bond",
        "TreasuryBill" => "TreasuryBill",
        "Option" => "OptionContract",
        "Future" => "FutureContract",
        "Swap" => "SwapContract",
        "DirectLoan" => "DirectLoan",
        "Deposit" => "Deposit",
        "MoneyMarketFund" => "MoneyMarket",
        "CertificateOfDeposit" => "CertificateOfDeposit",
        "CommercialPaper" => "CommercialPaper",
        "Repo" => "Repo",
        _ => null
    };

    private static async Task SeedBreakQueueAsync(
        IServiceProvider services,
        IReadOnlyList<StrategyRunSummary> runs,
        IReadOnlyList<ReconciliationRunDetail?> reconciliations,
        CancellationToken ct)
    {
        var repository = services.GetService<IReconciliationBreakQueueRepository>();
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
                        RecommendedAction: "ReviewAndResolve"),
                    ct).ConfigureAwait(false);
            }
        }
    }

    private sealed record ReconciliationExceptionRouting(
        string ExceptionRoute,
        string ToleranceProfileId,
        decimal ToleranceBand,
        string RequiredSignoffRole,
        string SignoffStatus);

    private static ReconciliationExceptionRouting ResolveReconciliationExceptionRouting(
        ReconciliationBreakCategory category,
        ReconciliationBreakSeverity severity,
        decimal variance)
    {
        if (severity == ReconciliationBreakSeverity.Critical)
        {
            return new ReconciliationExceptionRouting(
                ExceptionRoute: category is ReconciliationBreakCategory.MissingLedgerCoverage or ReconciliationBreakCategory.MissingBankCoverage
                    ? "governance-coverage-escalation"
                    : "governance-variance-escalation",
                ToleranceProfileId: "critical-zero-tolerance",
                ToleranceBand: 0m,
                RequiredSignoffRole: "Governance sign-off",
                SignoffStatus: "pending-signoff");
        }

        if (severity == ReconciliationBreakSeverity.High)
        {
            return new ReconciliationExceptionRouting(
                ExceptionRoute: "fund-ops-review",
                ToleranceProfileId: "high-variance-watch",
                ToleranceBand: Math.Max(100m, Math.Round(variance * 0.05m, 2)),
                RequiredSignoffRole: "Fund operations lead",
                SignoffStatus: "pending-signoff");
        }

        if (category is ReconciliationBreakCategory.ClassificationGap or ReconciliationBreakCategory.MissingPortfolioCoverage)
        {
            return new ReconciliationExceptionRouting(
                ExceptionRoute: "security-master-governance-review",
                ToleranceProfileId: "coverage-classification-review",
                ToleranceBand: 0m,
                RequiredSignoffRole: "Governance analyst",
                SignoffStatus: "routing-review");
        }

        if (severity == ReconciliationBreakSeverity.Low || severity == ReconciliationBreakSeverity.Info)
        {
            return new ReconciliationExceptionRouting(
                ExceptionRoute: "ops-monitor",
                ToleranceProfileId: "low-variance-watch",
                ToleranceBand: 500m,
                RequiredSignoffRole: "Operations reviewer",
                SignoffStatus: "monitor");
        }

        return new ReconciliationExceptionRouting(
            ExceptionRoute: "operations-triage",
            ToleranceProfileId: "standard-recon-tolerance",
            ToleranceBand: Math.Max(250m, Math.Round(variance * 0.02m, 2)),
            RequiredSignoffRole: "Operations reviewer",
            SignoffStatus: "pending-signoff");
    }

    private static async Task EnsureBreakQueueSeededAsync(IServiceProvider services, CancellationToken ct)
    {
        var readService = services.GetService<StrategyRunReadService>();
        var reconciliationService = services.GetService<IReconciliationRunService>();
        if (readService is null || reconciliationService is null)
        {
            return;
        }

        var runs = await readService.GetRunsAsync(ct: ct).ConfigureAwait(false);
        if (runs.Count == 0)
        {
            return;
        }

        var reconciliations = await Task.WhenAll(
            runs.Select(run => reconciliationService.GetLatestForRunAsync(run.RunId, ct))).ConfigureAwait(false);
        await SeedBreakQueueAsync(services, runs, reconciliations, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<ReconciliationBreakQueueItem>> GetBreakQueueItemsAsync(
        IServiceProvider services,
        string? status,
        string? fundAccountId,
        CancellationToken ct)
    {
        var repository = services.GetService<IReconciliationBreakQueueRepository>();
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
        if (string.IsNullOrWhiteSpace(fundAccountId))
        {
            return items;
        }

        return items.Where(item => string.Equals(item.FundAccountId, fundAccountId, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

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
        var signedOffCount = items.Count(static item => IsSignedOff(item.SignoffStatus));
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
            Profiles: profiles);
    }

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
            SignedOffCount: items.Count(static item => IsSignedOff(item.SignoffStatus)),
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

        return "All reconciliation breaks are resolved or dismissed; calibration is ready for governance sign-off.";
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

    private static bool IsSignedOff(string? signoffStatus)
        => string.Equals(signoffStatus, "signed-off", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCalibrationValue(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static async Task<ReconciliationBreakQueueTransitionResult> ReviewBreakAsync(
        IServiceProvider services,
        ReviewReconciliationBreakRequest request,
        CancellationToken ct)
    {
        var repository = services.GetService<IReconciliationBreakQueueRepository>();
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
        if (repository is null)
        {
            return new ReconciliationBreakQueueTransitionResult(
                ReconciliationBreakQueueTransitionStatus.NotFound,
                Item: null,
                Error: "Reconciliation break queue repository is not registered.");
        }

        return await repository.ResolveAsync(request, ct).ConfigureAwait(false);
    }

    private static void MapStrategyDesignerEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet("/strategy/designer/templates", (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<StrategyDesignService>();
            return service is null
                ? StrategyDesignerUnavailable(jsonOptions)
                : Results.Json(service.GetTemplates(), jsonOptions);
        })
        .WithName("GetStrategyDesignerTemplates")
        .Produces<IReadOnlyList<StrategyDesignTemplate>>(200)
        .Produces(501);

        group.MapGet("/strategy/designer/field-catalog", (HttpContext context) =>
        {
            var service = context.RequestServices.GetService<StrategyDesignService>();
            return service is null
                ? StrategyDesignerUnavailable(jsonOptions)
                : Results.Json(service.GetFieldCatalog(), jsonOptions);
        })
        .WithName("GetStrategyDesignerFieldCatalog")
        .Produces<IReadOnlyList<StrategyDesignFieldCatalogItem>>(200)
        .Produces(501);

        group.MapGet("/strategy/designer/drafts", async (HttpContext context) =>
        {
            var repository = context.RequestServices.GetService<IStrategyDesignRepository>();
            if (repository is null)
            {
                return StrategyDesignerUnavailable(jsonOptions);
            }

            var drafts = await repository.ListDraftsAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Json(drafts, jsonOptions);
        })
        .WithName("GetStrategyDesignerDrafts")
        .Produces<IReadOnlyList<StrategyDesignDraftSummary>>(200)
        .Produces(501);

        group.MapGet("/strategy/designer/drafts/{documentId}", async (string documentId, HttpContext context) =>
        {
            var repository = context.RequestServices.GetService<IStrategyDesignRepository>();
            if (repository is null)
            {
                return StrategyDesignerUnavailable(jsonOptions);
            }

            var document = await repository.GetAsync(documentId, context.RequestAborted).ConfigureAwait(false);
            return document is null
                ? Results.NotFound(new { error = "Strategy design draft was not found." })
                : Results.Json(document, jsonOptions);
        })
        .WithName("GetStrategyDesignerDraft")
        .Produces<StrategyDesignDocument>(200)
        .Produces(404)
        .Produces(501);

        group.MapPost("/strategy/designer/drafts", async (StrategyDesignDraftSaveRequest? request, HttpContext context) =>
        {
            if (!HasPermission(context, UserPermission.ManageStrategies))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (request?.Document is null)
            {
                return Results.BadRequest(new { error = "A strategy design document is required." });
            }

            var service = context.RequestServices.GetService<StrategyDesignService>();
            var repository = context.RequestServices.GetService<IStrategyDesignRepository>();
            if (service is null || repository is null)
            {
                return StrategyDesignerUnavailable(jsonOptions);
            }

            var document = service.Normalize(request.Document);
            var validation = service.Validate(document);
            await repository.SaveAsync(document, context.RequestAborted).ConfigureAwait(false);
            var response = new StrategyDesignDraftSaveResponse(
                document,
                StrategyDesignService.CreateDraftSummary(document),
                validation,
                service.BuildRunTrace(document, validation));
            return Results.Json(response, jsonOptions);
        })
        .WithName("SaveStrategyDesignerDraft")
        .Produces<StrategyDesignDraftSaveResponse>(200)
        .Produces(400)
        .Produces(403)
        .Produces(501);

        group.MapPost("/strategy/designer/validate", (StrategyDesignDocument? document, HttpContext context) =>
        {
            if (document is null)
            {
                return Results.BadRequest(new { error = "A strategy design document is required." });
            }

            var service = context.RequestServices.GetService<StrategyDesignService>();
            if (service is null)
            {
                return StrategyDesignerUnavailable(jsonOptions);
            }

            var normalized = service.Normalize(document);
            return Results.Json(service.Validate(normalized), jsonOptions);
        })
        .WithName("ValidateStrategyDesignerDocument")
        .Produces<StrategyDesignValidationResult>(200)
        .Produces(400)
        .Produces(501);

        group.MapPost("/strategy/designer/preview", (StrategyDesignDocument? document, HttpContext context) =>
        {
            if (document is null)
            {
                return Results.BadRequest(new { error = "A strategy design document is required." });
            }

            var service = context.RequestServices.GetService<StrategyDesignService>();
            if (service is null)
            {
                return StrategyDesignerUnavailable(jsonOptions);
            }

            var normalized = service.Normalize(document);
            var preview = service.Preview(normalized);
            return preview.Validation.IsValid
                ? Results.Json(preview, jsonOptions)
                : Results.Json(preview, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
        })
        .WithName("PreviewStrategyDesignerDocument")
        .Produces<StrategyDesignPreviewResult>(200)
        .Produces(400)
        .Produces(501);

        group.MapPost("/strategy/designer/run-backtest", async (
            StrategyDesignRunBacktestRequest? request,
            HttpContext context,
            CancellationToken ct) =>
        {
            if (!HasPermission(context, UserPermission.ManageStrategies))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (request?.Document is null)
            {
                return Results.BadRequest(new { error = "A strategy design document is required." });
            }

            var service = context.RequestServices.GetService<StrategyDesignService>();
            if (service is null)
            {
                return StrategyDesignerUnavailable(jsonOptions);
            }

            var document = service.Normalize(request.Document);
            var preview = service.Preview(document);
            if (!preview.Validation.IsValid)
            {
                return Results.Json(
                    CreateBacktestResponse(document, preview, null, new Dictionary<string, string>(), "Validation failed."),
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var runner = context.RequestServices.GetService<IScriptRunner>();
            if (runner is null)
            {
                return Results.Json(
                    new
                    {
                        error = "Quant Lab is not enabled on this host. Set QuantLab:Enabled to true to enable.",
                        quantLabEnabled = false
                    },
                    jsonOptions,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var parameters = request.Parameters is null
                ? new Dictionary<string, object?>()
                : request.Parameters.ToDictionary(
                    static item => item.Key,
                    static item => (object?)item.Value,
                    StringComparer.OrdinalIgnoreCase);
            var result = await runner.RunAsync(preview.Compiled.Source, parameters, ct).ConfigureAwait(false);
            var metrics = result.Metrics.ToDictionary(
                static item => item.Key,
                static item => item.Value,
                StringComparer.OrdinalIgnoreCase);
            if (!result.Success)
            {
                return Results.Json(
                    CreateBacktestResponse(document, preview, null, metrics, result.RuntimeError),
                    jsonOptions,
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var runId = Guid.NewGuid().ToString("N");
            var repository = context.RequestServices.GetService<IStrategyRepository>();
            if (repository is not null)
            {
                var entry = StrategyRunEntry
                    .Start(
                        document.DocumentId,
                        document.Name,
                        RunType.Backtest,
                        runId,
                        datasetReference: document.DatasetReference,
                        feedReference: "strategy-designer:v1",
                        engine: "QuantScript",
                        parameterSet: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["designerDocumentId"] = document.DocumentId,
                            ["datasetFingerprint"] = preview.Compiled.DatasetFingerprint,
                            ["cellCount"] = document.Cells.Count.ToString(CultureInfo.InvariantCulture)
                        })
                    .Complete(result.CapturedBacktests.FirstOrDefault());
                await repository.RecordRunAsync(entry, ct).ConfigureAwait(false);
            }

            return Results.Json(CreateBacktestResponse(document, preview, runId, metrics, null), jsonOptions);
        })
        .WithName("RunStrategyDesignerBacktest")
        .Produces<StrategyDesignRunBacktestResponse>(200)
        .Produces(400)
        .Produces(403)
        .Produces(503);
    }

    private static StrategyDesignRunBacktestResponse CreateBacktestResponse(
        StrategyDesignDocument document,
        StrategyDesignPreviewResult preview,
        string? runId,
        IReadOnlyDictionary<string, string> metrics,
        string? runtimeError)
    {
        var success = runId is not null && runtimeError is null;
        var trace = preview.Trace
            .Concat([
                new StrategyDesignRunTraceEntry(
                    "record-run",
                    "Record StrategyRunEntry",
                    success ? "complete" : "blocked",
                    success
                        ? $"Recorded backtest run {runId} for promotion review."
                        : runtimeError ?? "Backtest did not produce a recorded run.",
                    OccurredAt: DateTimeOffset.UtcNow)
            ])
            .ToArray();

        return new StrategyDesignRunBacktestResponse(
            success,
            runId,
            document.DocumentId,
            document.Name,
            preview.Validation,
            preview.Compiled,
            trace,
            preview.Rows,
            metrics,
            runtimeError,
            success ? $"/api/promotion/evaluate/{runId}" : null,
            success ? $"/api/workstation/runs/{runId}/review-packet" : null);
    }

    private static IResult StrategyDesignerUnavailable(JsonSerializerOptions jsonOptions)
        => Results.Json(
            new { error = "Strategy Designer services are not registered." },
            jsonOptions,
            statusCode: StatusCodes.Status501NotImplemented);

    private static bool HasPermission(HttpContext context, UserPermission requiredPermission)
        => EndpointAuthorization.HasPermission(context, requiredPermission);

    private static bool HasReconciliationMutationPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ManageDirectLending,
            UserPermission.ModifySecurityMaster);

    private static bool HasSecurityMasterOverrideApprovalPermission(HttpContext context)
        => EndpointAuthorization.HasAnyPermission(
            context,
            UserPermission.AdminMaintenance,
            UserPermission.ModifySecurityMaster);

    private static bool HasGovernedWorkflowReopenPermission(HttpContext context)
        => EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance);

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

    private static void MapStrategyEngineEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet("/strategy/engine/definitions", (HttpContext context) =>
        {
            var registry = context.RequestServices.GetService<StrategyEngineRegistry>();
            return registry is null
                ? StrategyEngineUnavailable(jsonOptions)
                : Results.Json(registry.GetDefinitions(), jsonOptions);
        })
        .WithName("GetStrategyEngineDefinitions")
        .Produces<IReadOnlyList<StrategyEngineDefinition>>(200)
        .Produces(501);

        group.MapPost("/strategy/engine/validate-run", (
            StrategyEngineValidateRunRequest? request,
            HttpContext context) =>
        {
            if (request?.RunRequest is null)
            {
                return Results.BadRequest(new { error = "A strategy run request is required." });
            }

            var validation = context.RequestServices.GetService<StrategyEngineValidationService>();
            if (validation is null)
            {
                return StrategyEngineUnavailable(jsonOptions);
            }

            var result = validation.Validate(request.RunRequest, request.DataAvailability ?? []);
            return result.IsValid
                ? Results.Json(result, jsonOptions)
                : Results.Json(result, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
        })
        .WithName("ValidateStrategyEngineRun")
        .Produces<StrategyEngineValidationResult>(200)
        .Produces<StrategyEngineValidationResult>(400)
        .Produces(501);
    }

    private static IResult StrategyEngineUnavailable(JsonSerializerOptions jsonOptions)
        => Results.Json(
            new { error = "Strategy Engine services are not registered." },
            jsonOptions,
            statusCode: StatusCodes.Status501NotImplemented);

    private sealed record StrategyEngineValidateRunRequest(
        StrategyEngineRunRequest RunRequest,
        IReadOnlyList<StrategyEngineDataAvailability>? DataAvailability);

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
    private sealed record SecurityCoverageReferencePayload(
        string Source,
        string Symbol,
        string? AccountName,
        string? SecurityId,
        string DisplayName,
        string? AssetClass,
        string? SubType,
        string? Currency,
        string? Status,
        string? PrimaryIdentifier,
        string CoverageStatus,
        string? CoverageReason,
        string? MatchedIdentifierKind,
        string? MatchedIdentifierValue,
        string? MatchedProvider);

    private sealed record SecurityCoverageGapPayload(
        string Source,
        string Symbol,
        string? AccountName,
        string Reason);


    private sealed record ProviderTrustRationalePayload(
        string Status,
        string TrustScore,
        string SignalSource,
        string ReasonCode,
        string RecommendedAction,
        string GateImpact);
}

/// <summary>Request to compare multiple strategy runs side by side.</summary>
public sealed record RunComparisonRequest(
    IReadOnlyList<string> RunIds,
    IReadOnlyList<string>? Modes = null);

/// <summary>Request to diff two strategy runs.</summary>
public sealed record RunDiffRequest(string BaseRunId, string TargetRunId);

/// <summary>Result of a run-vs-run diff showing position, parameter, and metric changes.</summary>
public sealed record StrategyRunDiff(
    string BaseRunId,
    string TargetRunId,
    string BaseStrategyName,
    string TargetStrategyName,
    IReadOnlyList<PositionDiffEntry> AddedPositions,
    IReadOnlyList<PositionDiffEntry> RemovedPositions,
    IReadOnlyList<PositionDiffEntry> ModifiedPositions,
    IReadOnlyList<ParameterDiff> ParameterChanges,
    MetricsDiff Metrics);

/// <summary>A single position change between two runs.</summary>
public sealed record PositionDiffEntry(
    string Symbol,
    long BaseQuantity,
    long TargetQuantity,
    decimal BasePnl,
    decimal TargetPnl,
    string ChangeType);

/// <summary>A single parameter change between two runs.</summary>
public sealed record ParameterDiff(
    string Key,
    string? BaseValue,
    string? TargetValue);

/// <summary>High-level metrics delta between two runs.</summary>
public sealed record MetricsDiff(
    decimal NetPnlDelta,
    decimal TotalReturnDelta,
    int FillCountDelta,
    decimal? BaseNetPnl,
    decimal? TargetNetPnl,
    decimal? BaseTotalReturn,
    decimal? TargetTotalReturn);
