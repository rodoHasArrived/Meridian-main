using System.Text.Json;
using Meridian.Application.OperationsContinuity;
using Meridian.Contracts.Api;
using Meridian.Contracts.Auth;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private sealed record ReconciliationBreakBulkActionRequest(
        IReadOnlyList<string> BreakIds,
        string Action,
        string? Actor = null,
        string? Assignee = null,
        ReconciliationBreakQueueStatus? Status = null,
        string? CommentTemplate = null);

    private static void MapReconciliationEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationRuns), async (
            ReconciliationRunRequest request,
            HttpContext context,
            [FromServices] IReconciliationRunService? service) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

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
        .Produces(403)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationRunById), async (
            string reconciliationRunId,
            HttpContext context,
            [FromServices] IReconciliationRunService? service) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

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

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsReconciliation), async (
            string runId,
            HttpContext context,
            [FromServices] IReconciliationRunService? service) =>
        {
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

        group.MapGet(WorkstationSubroute(UiApiRoutes.RunsReconciliationHistory), async (
            string runId,
            HttpContext context,
            [FromServices] IReconciliationRunService? service) =>
        {
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

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementRuns), async ([FromServices] IReconciliationApiService? service, HttpContext context) =>
        {
            if (service is null) return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            return Results.Json(await service.ListStatementRunsAsync(context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("ListStatementRuns")
        .Produces<IReadOnlyList<StatementRunSummaryDto>>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementRunById), async (string runId, [FromServices] IReconciliationApiService? service, HttpContext context) =>
        {
            if (service is null) return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            var detail = await service.GetStatementRunAsync(runId, context.RequestAborted).ConfigureAwait(false);
            return detail is null ? Results.NotFound() : Results.Json(detail, jsonOptions);
        })
        .WithName("GetStatementRun")
        .Produces<StatementRunSummaryDto>(200)
        .Produces(404);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementExceptions), async ([FromServices] IReconciliationApiService? service, HttpContext context) =>
        {
            if (service is null) return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            return Results.Json(await service.ListOpenExceptionsAsync(context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("ListStatementExceptions")
        .Produces<IReadOnlyList<StatementRunExceptionDto>>(200);

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


        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakQueue), async (
            string? status,
            string? fundAccountId,
            string? team,
            string? assignee,
            string? counterparty,
            HttpContext context,
            [FromServices] StrategyRunReadService? readService,
            [FromServices] IReconciliationRunService? reconciliationService,
            [FromServices] IReconciliationApiService? statementService,
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            await EnsureBreakQueueSeededAsync(readService, reconciliationService, statementService, repository, context.RequestAborted).ConfigureAwait(false);
            var items = await GetBreakQueueItemsAsync(repository, status, fundAccountId, context.RequestAborted).ConfigureAwait(false);
            items = items
                .Where(item => string.IsNullOrWhiteSpace(team) || string.Equals(item.Team, team, StringComparison.OrdinalIgnoreCase))
                .Where(item => string.IsNullOrWhiteSpace(assignee) || string.Equals(item.AssignedTo, assignee, StringComparison.OrdinalIgnoreCase))
                .Where(item => string.IsNullOrWhiteSpace(counterparty) || string.Equals(item.Counterparty, counterparty, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return Results.Json(items, jsonOptions);
        })
        .WithName("GetReconciliationBreakQueue")
        .Produces<IReadOnlyList<ReconciliationBreakQueueItem>>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakQueueById), async (
            string breakId,
            HttpContext context,
            [FromServices] StrategyRunReadService? readService,
            [FromServices] IReconciliationRunService? reconciliationService,
            [FromServices] IReconciliationApiService? statementService,
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            await EnsureBreakQueueSeededAsync(readService, reconciliationService, statementService, repository, context.RequestAborted).ConfigureAwait(false);
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

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationCalibrationSummary), async (
            HttpContext context,
            [FromServices] StrategyRunReadService? readService,
            [FromServices] IReconciliationRunService? reconciliationService,
            [FromServices] IReconciliationApiService? statementService,
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            var asOf = DateTimeOffset.UtcNow;
            await EnsureBreakQueueSeededAsync(readService, reconciliationService, statementService, repository, context.RequestAborted).ConfigureAwait(false);
            var items = await GetBreakQueueItemsAsync(repository, status: null, fundAccountId: null, context.RequestAborted).ConfigureAwait(false);
            var summary = BuildReconciliationCalibrationSummary(items, asOf);
            return Results.Json(summary, jsonOptions);
        })
        .WithName("GetReconciliationCalibrationSummary")
        .Produces<ReconciliationCalibrationSummaryDto>(200);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakAudit), async (
            string breakId,
            HttpContext context,
            [FromServices] StrategyRunReadService? readService,
            [FromServices] IReconciliationRunService? reconciliationService,
            [FromServices] IReconciliationApiService? statementService,
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            await EnsureBreakQueueSeededAsync(readService, reconciliationService, statementService, repository, context.RequestAborted).ConfigureAwait(false);
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

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakReview), async (
            string breakId,
            ReviewReconciliationBreakRequest request,
            HttpContext context,
            [FromServices] StrategyRunReadService? readService,
            [FromServices] IReconciliationRunService? reconciliationService,
            [FromServices] IReconciliationApiService? statementService,
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
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

            await EnsureBreakQueueSeededAsync(readService, reconciliationService, statementService, repository, context.RequestAborted).ConfigureAwait(false);
            var transition = await ReviewBreakAsync(repository, trustedRequest, context.RequestAborted).ConfigureAwait(false);
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

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakResolve), async (
            string breakId,
            ResolveReconciliationBreakRequest request,
            HttpContext context,
            [FromServices] StrategyRunReadService? readService,
            [FromServices] IReconciliationRunService? reconciliationService,
            [FromServices] IReconciliationApiService? statementService,
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
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

            await EnsureBreakQueueSeededAsync(readService, reconciliationService, statementService, repository, context.RequestAborted).ConfigureAwait(false);
            var transition = await ResolveBreakAsync(repository, trustedRequest, context.RequestAborted).ConfigureAwait(false);
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


        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakQueue) + "/bulk", async (
            ReconciliationBreakBulkActionRequest request,
            HttpContext context,
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            if (!HasReconciliationMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (repository is null)
            {
                return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var actor = string.IsNullOrWhiteSpace(request.Actor) && TryResolveCurrentUser(context, out var currentUser) ? currentUser : request.Actor ?? "system";
            var updated = new List<ReconciliationBreakQueueItem>();
            foreach (var breakId in request.BreakIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var item = await repository.GetByIdAsync(breakId, context.RequestAborted).ConfigureAwait(false);
                if (item is null)
                {
                    continue;
                }

                var next = item;
                if (string.Equals(request.Action, "assign", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(request.Assignee))
                {
                    next = item with { AssignedTo = request.Assignee, LifecycleState = ReconciliationCaseLifecycleState.InReview, LastUpdatedAt = DateTimeOffset.UtcNow, LifecycleRationale = request.CommentTemplate ?? "Bulk assigned" };
                }
                else if (string.Equals(request.Action, "status", StringComparison.OrdinalIgnoreCase) && request.Status.HasValue)
                {
                    next = item with { Status = request.Status.Value, LastUpdatedAt = DateTimeOffset.UtcNow, LifecycleRationale = request.CommentTemplate ?? "Bulk status update" };
                }
                else if (string.Equals(request.Action, "comment", StringComparison.OrdinalIgnoreCase))
                {
                    next = item with { ResolutionNote = request.CommentTemplate, LastUpdatedAt = DateTimeOffset.UtcNow };
                }

                await repository.SaveAsync(next, context.RequestAborted).ConfigureAwait(false);
                updated.Add(next);
            }

            return Results.Json(updated, jsonOptions);
        })
        .WithName("BulkUpdateReconciliationBreaks")
        .Produces<IReadOnlyList<ReconciliationBreakQueueItem>>(200)
        .Produces(403)
        .Produces(501);

    }
}
