using System.Text.Json;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class WorkstationEndpoints
{
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

        // Authentication proves the executing operator. Origin and privilege are always derived
        // server-side; independent approval evidence remains an explicit retained input and is
        // normalized here before the repository validates its presence and actor separation.
        var trusted = request with
        {
            Actor = currentUser,
            Source = "workstation-reconciliation-casework",
            ActionOrigin = OperationsActionOriginDto.HumanOperator,
            Privileged = HasGovernedWorkflowReopenPermission(context),
            ApprovalActor = NormalizeApprovalEvidence(request.ApprovalActor),
            ApprovalReference = NormalizeApprovalEvidence(request.ApprovalReference)
        };
        var transition = await repository.ApplyCaseworkCommandAsync(trusted, context.RequestAborted).ConfigureAwait(false);
        return Results.Json(ToReconciliationCaseworkOperationResult(transition), jsonOptions);
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

        var trusted = request with
        {
            Actor = currentUser,
            Source = "workstation-reconciliation-bulk-casework",
            ActionOrigin = OperationsActionOriginDto.HumanOperator,
            ApprovalActor = NormalizeApprovalEvidence(request.ApprovalActor),
            ApprovalReference = NormalizeApprovalEvidence(request.ApprovalReference)
        };
        var result = await repository.ApplyBulkCaseworkAsync(trusted, context.RequestAborted).ConfigureAwait(false);
        return Results.Json(result, jsonOptions);
    }

    private static ReconciliationCaseworkOperationResult ToReconciliationCaseworkOperationResult(
        ReconciliationBreakQueueTransitionResult transition)
        => new(
            transition.Status.ToString(),
            transition.Item,
            transition.Outcome,
            transition.Error,
            transition.ErrorCode.ToString(),
            transition.Validation?.MissingFields);

    private static string? NormalizeApprovalEvidence(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}
