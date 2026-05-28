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

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
    private sealed record LegacyReconciliationBreakBulkActionRequest(
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

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakQueue), async (
            string? status,
            string? fundAccountId,
            string? team,
            string? assignee,
            string? counterparty,
            HttpContext context,
            [FromServices] StrategyRunReadService? readService,
            [FromServices] IReconciliationRunService? reconciliationService,
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            await EnsureBreakQueueSeededAsync(readService, reconciliationService, repository, context.RequestAborted).ConfigureAwait(false);
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
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            await EnsureBreakQueueSeededAsync(readService, reconciliationService, repository, context.RequestAborted).ConfigureAwait(false);
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
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            var asOf = DateTimeOffset.UtcNow;
            await EnsureBreakQueueSeededAsync(readService, reconciliationService, repository, context.RequestAborted).ConfigureAwait(false);
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
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            await EnsureBreakQueueSeededAsync(readService, reconciliationService, repository, context.RequestAborted).ConfigureAwait(false);
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

            await EnsureBreakQueueSeededAsync(readService, reconciliationService, repository, context.RequestAborted).ConfigureAwait(false);
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

            await EnsureBreakQueueSeededAsync(readService, reconciliationService, repository, context.RequestAborted).ConfigureAwait(false);
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


        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakAssign), async (
            string breakId,
            ReconciliationAssignRequest request,
            HttpContext context,
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            if (!HasReconciliationMutationPermission(context)) return EndpointHelpers.Forbidden();
            if (!TryResolveCurrentUser(context, out var currentUser)) return Results.Unauthorized();
            if (!string.Equals(request.BreakId, breakId, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "BreakId in body must match route parameter." });
            return ReconciliationTransitionHttpResult(await RequireRepository(repository).AssignAsync(request, currentUser, ResolveCorrelationId(context), source: "workstation-api", ct: context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("AssignReconciliationBreak")
        .Produces<ReconciliationBreakQueueItem>(200)
        .Produces(400)
        .Produces(401)
        .Produces(403)
        .Produces(404)
        .Produces(409);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakTransition), async (
            string breakId,
            ReconciliationStatusTransitionRequest request,
            HttpContext context,
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            if (!HasReconciliationMutationPermission(context)) return EndpointHelpers.Forbidden();
            if (!TryResolveCurrentUser(context, out var currentUser)) return Results.Unauthorized();
            if (!string.Equals(request.BreakId, breakId, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "BreakId in body must match route parameter." });
            return ReconciliationTransitionHttpResult(await RequireRepository(repository).TransitionStatusAsync(request, currentUser, ResolveCorrelationId(context), source: "workstation-api", ct: context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("TransitionReconciliationBreak")
        .Produces<ReconciliationBreakQueueItem>(200)
        .Produces(400)
        .Produces(401)
        .Produces(403)
        .Produces(404)
        .Produces(409);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakComments), async (
            string breakId,
            ReconciliationCommentMutationRequest request,
            HttpContext context,
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            if (!HasReconciliationMutationPermission(context)) return EndpointHelpers.Forbidden();
            if (!TryResolveCurrentUser(context, out var currentUser)) return Results.Unauthorized();
            if (!string.Equals(request.BreakId, breakId, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "BreakId in body must match route parameter." });
            return ReconciliationTransitionHttpResult(await RequireRepository(repository).AddCommentAsync(request, currentUser, currentUser, ResolveCorrelationId(context), source: "workstation-api", ct: context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("AddReconciliationBreakComment")
        .Produces<ReconciliationBreakQueueItem>(200)
        .Produces(400)
        .Produces(401)
        .Produces(403)
        .Produces(404)
        .Produces(409);

        group.MapPut(WorkstationSubroute(UiApiRoutes.ReconciliationBreakCommentById), async (
            string breakId,
            string commentId,
            ReconciliationCommentMutationRequest request,
            HttpContext context,
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            if (!HasReconciliationMutationPermission(context)) return EndpointHelpers.Forbidden();
            if (!TryResolveCurrentUser(context, out var currentUser)) return Results.Unauthorized();
            if (!string.Equals(request.BreakId, breakId, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "BreakId in body must match route parameter." });
            return ReconciliationTransitionHttpResult(await RequireRepository(repository).EditCommentAsync(request with { CommentId = commentId }, currentUser, ResolveCorrelationId(context), source: "workstation-api", ct: context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("EditReconciliationBreakComment")
        .Produces<ReconciliationBreakQueueItem>(200)
        .Produces(400)
        .Produces(401)
        .Produces(403)
        .Produces(404)
        .Produces(409);

        group.MapDelete(WorkstationSubroute(UiApiRoutes.ReconciliationBreakCommentById), async (
            string breakId,
            string commentId,
            HttpContext context,
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            if (!HasReconciliationMutationPermission(context)) return EndpointHelpers.Forbidden();
            if (!TryResolveCurrentUser(context, out var currentUser)) return Results.Unauthorized();
            var request = new ReconciliationCommentMutationRequest(breakId, commentId, null, string.Empty, ReconciliationCommentVisibility.Internal, Reason: "Comment deleted.");
            return ReconciliationTransitionHttpResult(await RequireRepository(repository).DeleteCommentAsync(request, currentUser, ResolveCorrelationId(context), source: "workstation-api", ct: context.RequestAborted).ConfigureAwait(false), jsonOptions);
        })
        .WithName("DeleteReconciliationBreakComment")
        .Produces<ReconciliationBreakQueueItem>(200)
        .Produces(400)
        .Produces(401)
        .Produces(403)
        .Produces(404)
        .Produces(409);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakRootCause), async (string breakId, ReconciliationTaxonomyRequest request, HttpContext context, [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            if (!HasReconciliationMutationPermission(context)) return EndpointHelpers.Forbidden();
            if (!TryResolveCurrentUser(context, out var currentUser)) return Results.Unauthorized();
            if (!string.Equals(request.BreakId, breakId, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "BreakId in body must match route parameter." });
            return ReconciliationTransitionHttpResult(await RequireRepository(repository).SetRootCauseAsync(request, currentUser, ResolveCorrelationId(context), source: "workstation-api", ct: context.RequestAborted).ConfigureAwait(false), jsonOptions);
        }).WithName("SetReconciliationBreakRootCause").Produces<ReconciliationBreakQueueItem>(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(409);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakResolution), async (string breakId, ReconciliationTaxonomyRequest request, HttpContext context, [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            if (!HasReconciliationMutationPermission(context)) return EndpointHelpers.Forbidden();
            if (!TryResolveCurrentUser(context, out var currentUser)) return Results.Unauthorized();
            if (!string.Equals(request.BreakId, breakId, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "BreakId in body must match route parameter." });
            return ReconciliationTransitionHttpResult(await RequireRepository(repository).SetResolutionAsync(request, currentUser, ResolveCorrelationId(context), source: "workstation-api", ct: context.RequestAborted).ConfigureAwait(false), jsonOptions);
        }).WithName("SetReconciliationBreakResolution").Produces<ReconciliationBreakQueueItem>(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(409);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakSignOff), async (string breakId, ReconciliationSignOffRequest request, HttpContext context, [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            if (!HasReconciliationMutationPermission(context)) return EndpointHelpers.Forbidden();
            if (!TryResolveCurrentUser(context, out var currentUser)) return Results.Unauthorized();
            if (!string.Equals(request.BreakId, breakId, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "BreakId in body must match route parameter." });
            return ReconciliationTransitionHttpResult(await RequireRepository(repository).SignOffAsync(request, currentUser, ResolveCorrelationId(context), source: "workstation-api", ct: context.RequestAborted).ConfigureAwait(false), jsonOptions);
        }).WithName("SignOffReconciliationBreak").Produces<ReconciliationBreakQueueItem>(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(409);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakReopen), async (string breakId, ReconciliationReopenRequest request, HttpContext context, [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            if (!HasReconciliationMutationPermission(context)) return EndpointHelpers.Forbidden();
            if (!TryResolveCurrentUser(context, out var currentUser)) return Results.Unauthorized();
            if (!string.Equals(request.BreakId, breakId, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "BreakId in body must match route parameter." });
            return ReconciliationTransitionHttpResult(await RequireRepository(repository).ReopenAsync(request, currentUser, ResolveCorrelationId(context), source: "workstation-api", ct: context.RequestAborted).ConfigureAwait(false), jsonOptions);
        }).WithName("ReopenReconciliationBreak").Produces<ReconciliationBreakQueueItem>(200).Produces(400).Produces(401).Produces(403).Produces(404).Produces(409);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakBulkDryRun), async (ReconciliationBulkActionRequest request, HttpContext context, [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            if (!HasReconciliationMutationPermission(context)) return EndpointHelpers.Forbidden();
            if (!TryResolveCurrentUser(context, out var currentUser)) return Results.Unauthorized();
            return Results.Json(await RequireRepository(repository).BulkActionAsync(request with { DryRun = true }, currentUser, ResolveCorrelationId(context), source: "workstation-api", ct: context.RequestAborted).ConfigureAwait(false), jsonOptions);
        }).WithName("DryRunReconciliationBreakBulkAction").Produces<ReconciliationBulkActionResult>(200).Produces(401).Produces(403);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakBulkExecute), async (ReconciliationBulkActionRequest request, HttpContext context, [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            if (!HasReconciliationMutationPermission(context)) return EndpointHelpers.Forbidden();
            if (!TryResolveCurrentUser(context, out var currentUser)) return Results.Unauthorized();
            return Results.Json(await RequireRepository(repository).BulkActionAsync(request with { DryRun = false }, currentUser, ResolveCorrelationId(context), source: "workstation-api", ct: context.RequestAborted).ConfigureAwait(false), jsonOptions);
        }).WithName("ExecuteReconciliationBreakBulkAction").Produces<ReconciliationBulkActionResult>(200).Produces(401).Produces(403);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationBreakBulkResult), async (string bulkActionId, HttpContext context, [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            var result = await RequireRepository(repository).GetBulkActionResultAsync(bulkActionId, context.RequestAborted).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Json(result, jsonOptions);
        }).WithName("GetReconciliationBreakBulkActionResult").Produces<ReconciliationBulkActionResult>(200).Produces(404);

        group.MapPost(WorkstationSubroute(UiApiRoutes.ReconciliationBreakQueue) + "/bulk", async (
            LegacyReconciliationBreakBulkActionRequest request,
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

    private static IReconciliationBreakQueueRepository RequireRepository(IReconciliationBreakQueueRepository? repository)
        => repository ?? throw new InvalidOperationException("Reconciliation break queue repository is not registered.");

    private static string? ResolveCorrelationId(HttpContext context)
        => context.TraceIdentifier;

    private static IResult ReconciliationTransitionHttpResult(ReconciliationBreakQueueTransitionResult transition, JsonSerializerOptions jsonOptions)
        => transition.Status switch
        {
            ReconciliationBreakQueueTransitionStatus.Success => Results.Json(transition.Item, jsonOptions),
            ReconciliationBreakQueueTransitionStatus.NotFound => Results.NotFound(),
            ReconciliationBreakQueueTransitionStatus.Conflict => Results.Conflict(new { error = transition.Error ?? "Case version conflict.", code = transition.ErrorCode.ToString() }),
            ReconciliationBreakQueueTransitionStatus.Unauthorized => Results.Unauthorized(),
            _ => Results.BadRequest(new { error = transition.Error ?? "Illegal transition.", code = transition.ErrorCode.ToString() })
        };

    }
}
