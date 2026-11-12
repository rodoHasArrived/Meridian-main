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
    private static void MapReconciliationRecordEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {

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
        .WithName("GetReconciliationBreakQueue").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ManageDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
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
        .WithName("GetReconciliationBreakQueueItem").RequireAnyPermission(UserPermission.ViewDirectLending, UserPermission.ManageDirectLending, UserPermission.ViewSecurityMaster, UserPermission.ModifySecurityMaster, UserPermission.AdminMaintenance)
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

            // Authentication proves the executing operator. Rewrite the client-supplied assignee the
            // same way as the reviewer: the browser lane hardcodes a placeholder assignee, the queue's
            // assignee filter must answer "my breaks", and the repository derives the audit actor from
            // AssignedTo ?? ReviewedBy ?? ResolvedBy — so neither identity may pass through verbatim.
            var reviewActor = ResolveCurrentActor(context);
            var transition = await ReviewBreakAsync(context.RequestServices, queueScope, request with
            {
                AssignedTo = reviewActor,
                ReviewedBy = reviewActor
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
                        ResolvedBy = ResolveCurrentActor(context),
                        // Derived from the principal, discarding the body: this is the statement
                        // legacy-resolve adapter, which is authoritative over the caller's identity
                        // like the rest of the reconciliation casework routes. Before #2673 the
                        // origin was left as the browser sent it. See DeriveActionOriginFromPrincipal.
                        ActionOrigin = EndpointAuthorization.DeriveActionOriginFromPrincipal(context)
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
    }
}
