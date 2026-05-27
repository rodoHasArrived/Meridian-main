using System.Text.Json;
using Meridian.Application.OperationsContinuity;
using Meridian.Contracts.Api;
using Meridian.Contracts.Auth;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private static void MapOperationsContinuityEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuity), async (
            Guid? fundAccountId,
            string? periodId,
            string? status,
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
        {
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
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

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuityById), async (
            Guid workflowId,
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
        {
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var workflow = await service.GetAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
            return workflow is null ? Results.NotFound() : Results.Json(workflow, jsonOptions);
        })
        .WithName("GetOperationsContinuityDetail");

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuityTimeline), async (
            Guid workflowId,
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
        {
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
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
            HttpContext context,
            [FromServices] IOperationsContinuityReconciliationBridge? service) =>
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
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
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
        {
            var isGovernedAdmin = EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance);
            if (!isGovernedAdmin)
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

            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var trustedRequest = request with { Actor = currentUser, IsGovernedAdmin = isGovernedAdmin };
            var result = await service.ReopenWorkflowAsync(workflowId, trustedRequest, context.RequestAborted).ConfigureAwait(false);
            return OperationsTransitionResult(result, jsonOptions);
        })
        .WithName("ReopenOperationsContinuityWorkflow")
        .Produces<OperationsTransitionResultDto>(200)
        .Produces<OperationsTransitionResultDto>(400)
        .Produces<OperationsTransitionResultDto>(409)
        .Produces(401)
        .Produces(403);

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuityBreaks), async (
            Guid workflowId,
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
        {
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var workflow = await service.GetAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
            return workflow is null ? Results.NotFound() : Results.Json(workflow.BreakCases, jsonOptions);
        })
        .WithName("GetOperationsContinuityBreaks");

        group.MapGet(WorkstationSubroute(UiApiRoutes.OperationsContinuityLedgerPreview), async (
            Guid workflowId,
            HttpContext context,
            [FromServices] IOperationsContinuityWorkflowService? service) =>
        {
            if (service is null)
            {
                return Results.Problem("Operations continuity workflow service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var workflow = await service.GetAsync(workflowId, context.RequestAborted).ConfigureAwait(false);
            return workflow is null ? Results.NotFound() : Results.Json(workflow.LedgerPreview, jsonOptions);
        })
        .WithName("GetOperationsContinuityLedgerPreview");
    }
}
