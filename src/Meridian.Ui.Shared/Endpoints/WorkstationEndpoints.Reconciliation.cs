using System.Text.Json;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.Accounts;
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
    private static bool LegacyReconciliationEndpointMapEnabled => false;

    private sealed record ReconciliationBreakBulkActionRequest(
        IReadOnlyList<string> BreakIds,
        string Action,
        string? Actor = null,
        string? Assignee = null,
        ReconciliationBreakQueueStatus? Status = null,
        string? CommentTemplate = null);

    private static void MapReconciliationEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        if (!LegacyReconciliationEndpointMapEnabled)
        {
            throw new InvalidOperationException(
                "The duplicate reconciliation endpoint mapper is retired. MapWorkstationEndpoints owns the authoritative reconciliation routes.");
        }

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
            HttpContext context) =>
            await CreateStatementRunAsync(request, context, jsonOptions).ConfigureAwait(false))
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
            return detail is null ? Results.NotFound() : Results.Json(detail, jsonOptions);
        })
        .WithName("ReconcileStatementRun")
        .Produces<StatementRunDto>(200)
        .Produces(401)
        .Produces(403)
        .Produces(404)
        .Produces(501);

        group.MapGet(WorkstationSubroute(UiApiRoutes.ReconciliationStatementExceptions), async ([FromServices] IReconciliationApiService? service, HttpContext context) =>
        {
            if (service is null)
                return Results.Problem("Reconciliation API service is not registered.", statusCode: StatusCodes.Status501NotImplemented);
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
            Guid? ledgerBookId,
            string? team,
            string? assignee,
            string? counterparty,
            HttpContext context,
            [FromServices] StrategyRunReadService? readService,
            [FromServices] IReconciliationRunService? reconciliationService,
            [FromServices] IReconciliationApiService? statementService,
            [FromServices] IReconciliationBreakQueueRepository? repository) =>
        {
            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var items = await GetBreakQueueItemsAsync(repository, queueScope, status, fundAccountId, ledgerBookId, context.RequestAborted).ConfigureAwait(false);
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
            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            if (repository is null)
            {
                return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var item = await repository.GetByIdAsync(queueScope, breakId, context.RequestAborted).ConfigureAwait(false);
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
            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var asOf = DateTimeOffset.UtcNow;
            var ledgerBookId = ParseOptionalGuid(context.Request.Query["ledgerBookId"].FirstOrDefault());
            var items = await GetBreakQueueItemsAsync(repository, queueScope, status: null, fundAccountId: null, ledgerBookId: ledgerBookId, ct: context.RequestAborted).ConfigureAwait(false);
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
            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            if (repository is null)
            {
                return Results.Problem("Reconciliation break queue repository is not registered.", statusCode: StatusCodes.Status501NotImplemented);
            }

            var history = await repository.GetAuditHistoryAsync(queueScope, breakId, context.RequestAborted).ConfigureAwait(false);
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

            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!string.Equals(request.BreakId, breakId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "BreakId in body must match route parameter." });
            }

            var trustedRequest = request with { ReviewedBy = currentUser };

            var transition = await ReviewBreakAsync(repository, queueScope, trustedRequest, context.RequestAborted).ConfigureAwait(false);
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

            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var trustedRequest = request with { ResolvedBy = currentUser };

            var transition = await ResolveBreakAsync(repository, queueScope, trustedRequest, context.RequestAborted).ConfigureAwait(false);
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

            if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
            {
                return EndpointHelpers.Forbidden();
            }

            var actor = string.IsNullOrWhiteSpace(request.Actor) && TryResolveCurrentUser(context, out var currentUser) ? currentUser : request.Actor ?? "system";
            var updated = new List<ReconciliationBreakQueueItem>();
            foreach (var breakId in request.BreakIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var item = await repository.GetByIdAsync(queueScope, breakId, context.RequestAborted).ConfigureAwait(false);
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

    private static async Task<IResult?> RequireStatementRunAccountAccessAsync(
        StatementRunCreateDto request,
        IAccountQueryService? accounts,
        HttpContext context)
    {
        // The retained-book provider treats FundAccountId as an authority to load internal positions
        // and cash. Resolve it server-side before importing so a reconciliation mutator cannot select
        // another account and receive its unmatched internal records as breaks.
        if (accounts is null || !Guid.TryParse(request.FundAccountId, out var accountId))
        {
            return EndpointHelpers.Forbidden();
        }

        var account = await accounts.GetAccountAsync(accountId, context.RequestAborted).ConfigureAwait(false);
        if (account is null || !account.IsActive || !IsStatementSourceBoundToAccount(request, account))
        {
            return EndpointHelpers.Forbidden();
        }

        if (EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance))
        {
            return null;
        }

        // Do not fall back to a broad role permission when scoped authorization is unavailable.
        // This route returns account-level reconciliation evidence, so the absence of the scoped
        // authorizer must fail closed rather than silently grant every reconciliation mutator access.
        if (context.RequestServices.GetService<IScopedAuthorizationService>() is null)
        {
            return EndpointHelpers.Forbidden();
        }

        var allowed = await EndpointAuthorization.HasScopedPermissionAsync(
            context,
            UserPermission.ManageDirectLending,
            AccessScopeKindDto.Account,
            accountId,
            context.RequestAborted).ConfigureAwait(false);
        return allowed ? null : EndpointHelpers.Forbidden();
    }

    private static async Task<IResult> CreateStatementRunAsync(
        StatementRunCreateDto request,
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        if (!HasReconciliationMutationPermission(context))
        {
            return EndpointHelpers.Forbidden();
        }

        if (!TryResolveCurrentUser(context, out var currentUser))
        {
            return Results.Unauthorized();
        }

        var service = context.RequestServices.GetService<IReconciliationApiService>();
        if (service is null)
        {
            return Results.Problem(
                "Reconciliation API service is not registered.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var accounts = context.RequestServices.GetService<IAccountQueryService>();
        var accountGuard = await RequireStatementRunAccountAccessAsync(request, accounts, context).ConfigureAwait(false);
        if (accountGuard is not null)
        {
            return accountGuard;
        }

        var trustedRequest = request with { ImportedBy = currentUser };
        var detail = await service.CreateStatementRunAsync(trustedRequest, context.RequestAborted).ConfigureAwait(false);
        return detail is null
            ? Results.NotFound()
            : Results.Json(detail, jsonOptions, statusCode: StatusCodes.Status201Created);
    }

    private static bool IsStatementSourceBoundToAccount(StatementRunCreateDto request, AccountSummaryDto account)
    {
        var externalAccountId = request.ExternalAccountId?.Trim();
        var sourceInstitution = request.SourceInstitution?.Trim();
        if (string.IsNullOrWhiteSpace(externalAccountId) || string.IsNullOrWhiteSpace(sourceInstitution))
        {
            return false;
        }

        var externalAccountMatches = new[]
        {
            account.AccountCode,
            account.CustodianDetails?.SubAccountNumber,
            account.BankDetails?.AccountNumber,
            account.BankDetails?.Iban
        }.Any(candidate => string.Equals(candidate?.Trim(), externalAccountId, StringComparison.OrdinalIgnoreCase));
        var institutionMatches = new[]
        {
            account.Institution,
            account.BankDetails?.BankName
        }.Any(candidate => string.Equals(candidate?.Trim(), sourceInstitution, StringComparison.OrdinalIgnoreCase));

        return externalAccountMatches && institutionMatches;
    }
}
