using System.Text.Json;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static Meridian.Contracts.Text.TextPrimitives;

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

        if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
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

        var casework = context.RequestServices.GetService<IStatementReconciliationCaseworkHandoffService>();
        if (casework is null)
        {
            return Results.Problem(
                "Authoritative reconciliation casework handoff is not registered.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        // Authentication proves the executing operator. Origin and privilege are always derived
        // server-side; independent approval evidence remains an explicit retained input and is
        // normalized here before the repository validates its presence and actor separation.
        // Origin is the narrower of the caller's declaration and the principal's standing. This
        // used to hardcode HumanOperator, which stamped an API-key caller as a human and satisfied
        // the very gate that exists to stop a service credential performing a material action, and
        // which also discarded automation that declared itself honestly (#2673).
        var trusted = request with
        {
            Actor = currentUser,
            Source = "workstation-reconciliation-casework",
            ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin),
            Privileged = HasGovernedWorkflowReopenPermission(context),
            ApprovalActor = NormalizeOptional(request.ApprovalActor),
            ApprovalReference = NormalizeOptional(request.ApprovalReference)
        };
        try
        {
            var transition = await casework.ApplyAsync(queueScope, trusted, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(ToReconciliationCaseworkOperationResult(transition), jsonOptions);
        }
        catch (StatementReconciliationCaseworkHandoffException exception)
        {
            return Results.Problem(
                detail: $"{exception.Code}: {exception.Message}",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Statement reconciliation casework handoff failed");
        }
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

        if (!TryResolveReconciliationBreakQueueScope(context, out var queueScope))
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

        var casework = context.RequestServices.GetService<IStatementReconciliationCaseworkHandoffService>();
        if (casework is null)
        {
            return Results.Problem(
                "Authoritative reconciliation casework handoff is not registered.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var trusted = request with
        {
            Actor = currentUser,
            Source = "workstation-reconciliation-bulk-casework",
            // Narrower of declaration and principal -- see the single-case handler above (#2673).
            ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin),
            ApprovalActor = NormalizeOptional(request.ApprovalActor),
            ApprovalReference = NormalizeOptional(request.ApprovalReference)
        };
        try
        {
            var result = await casework.ApplyBulkAsync(queueScope, trusted, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, jsonOptions);
        }
        catch (StatementReconciliationCaseworkHandoffException exception)
        {
            return Results.Problem(
                detail: $"{exception.Code}: {exception.Message}",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Statement reconciliation bulk handoff failed");
        }
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

}
