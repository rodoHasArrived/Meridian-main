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
using Meridian.Contracts.Reconciliation;
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

public static partial class WorkstationEndpoints
{
    // Domain registration keeps authorization, scope filters and registration order intact.
    private static void MapOperationsContinuityRecordEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {

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
            if (request.ReportPackReady == true ||
                (request.ReportPackReady != false && !string.IsNullOrWhiteSpace(request.ReportPackId)))
            {
                var retained = await ResolveOperationsReportPackAsync(context, service, workflowId, request.ReportPackId,
                    requireRetainedRevision: false).ConfigureAwait(false);
                if (!retained.IsReady)
                    return ReportPackAuthorityRefusal(retained, jsonOptions);
                trustedRequest = trustedRequest with
                {
                    ReportPackReady = retained.IsReady,
                    ReportPackId = retained.ReportPackId,
                    EvidenceLinks = (request.EvidenceLinks ?? [])
                        .Where(link => link.Source is not ("accounting-report-pack" or "accounting-report-package-revision"))
                        .Concat(retained.EvidenceLinks).ToArray()
                };
            }
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

            var trustedRequest = request with { Actor = currentUser, OverrideId = overrideId, ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin) };
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

            var trustedRequest = request with { Actor = currentUser, ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin) };
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

            var trustedRequest = request with { Actor = currentUser, ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin) };
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
                // Narrower of the caller's declaration and the principal's standing: hardcoding
                // HumanOperator stamped an API-key caller as a human, and discarded automation
                // that declared itself (#2673).
                ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin),
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

            var trustedRequest = request with { Actor = currentUser, ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin) };
            var reportSupport = await ResolveOperationsReportPackAsync(context, service, workflowId, trustedRequest.ReportPackId).ConfigureAwait(false);
            if (!reportSupport.IsReady)
                return ReportPackAuthorityRefusal(reportSupport, jsonOptions);
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

            var trustedRequest = request with { Actor = currentUser, Reviewer = currentUser, ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin) };
            var reportSupport = await ResolveOperationsReportPackAsync(context, service, workflowId, trustedRequest.ReportPackId).ConfigureAwait(false);
            if (!reportSupport.IsReady)
                return ReportPackAuthorityRefusal(reportSupport, jsonOptions);
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

            var trustedRequest = request with { Actor = currentUser, Reviewer = currentUser, ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin) };
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

            var trustedRequest = request with { Actor = currentUser, ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin) };
            var readinessRefusal = await ValidateClosePublicationReadinessAsync(context, workflowId, trustedRequest.ExpectedVersion, trustedRequest.CloseScope, jsonOptions).ConfigureAwait(false);
            if (readinessRefusal is not null)
                return readinessRefusal;
            var reportSupport = await ResolveOperationsReportPackAsync(context, service, workflowId, trustedRequest.ReportPackId).ConfigureAwait(false);
            if (!reportSupport.IsReady)
                return ReportPackAuthorityRefusal(reportSupport, jsonOptions);
            return await LedgerEndpoints.ExecuteClosePeriodLockAsync(
                new LockClosePeriodRequestDto(
                    workflowId, trustedRequest.ExpectedVersion, currentUser, trustedRequest.Rationale,
                    trustedRequest.ReportPackId,
                    EvidenceLinks: trustedRequest.EvidenceLinks?.Select(link => link.EvidenceId).ToArray(),
                    ChecklistControlApprovals: trustedRequest.ChecklistControlApprovals,
                    CorrelationId: trustedRequest.CorrelationId,
                    ClosePackageId: trustedRequest.ClosePackageId,
                    ClosePackageManifestId: trustedRequest.ClosePackageManifestId,
                    ClosePackageRetainedManifestRoute: trustedRequest.ClosePackageRetainedManifestRoute,
                    ActionOrigin: trustedRequest.ActionOrigin,
                    CloseScope: trustedRequest.CloseScope),
                context, jsonOptions, operationsEnvelope: true).ConfigureAwait(false);
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

            var trustedRequest = request with { Actor = currentUser, IsGovernedAdmin = HasGovernedWorkflowReopenPermission(context), ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin) };
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
    }
}
