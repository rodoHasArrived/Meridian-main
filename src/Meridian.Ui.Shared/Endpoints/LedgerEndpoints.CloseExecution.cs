using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Microsoft.AspNetCore.Http;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class LedgerEndpoints
{
    // Both workstation entry points must execute the same controller, tenancy, posting,
    // reconciliation-seal, and retained-reporting handoff path.
    internal static async Task<IResult> ExecuteClosePeriodLockAsync(
        LockClosePeriodRequestDto request, HttpContext context, JsonSerializerOptions jsonOptions,
        bool operationsEnvelope = false)
    {
        if (!HasLedgerMutationPermission(context))
        {
            return EndpointHelpers.Forbidden();
        }

        string? controllerRole = null;
        if (!request.PrepareClosingEntriesOnly &&
            !TryResolveControllerRole(context, out controllerRole))
        {
            return EndpointHelpers.Forbidden();
        }

        var service = ResolveAccountingCloseManagementService(context);
        if (service is null)
        {
            return ServiceUnavailable();
        }

        try
        {
            var scope = await ResolveCloseWorkflowTenantScopeAsync(context, service, request.WorkflowId).ConfigureAwait(false);
            if (!scope.IsAccessible)
            {
                return CloseWorkflowScopeDenied();
            }
            if (scope.Plan is null)
            {
                return Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." });
            }

            var actor = ResolveMutationActor(context, request.Actor);
            var result = await service
                .LockClosePeriodScopedAsync(
                    request with
                    {
                        Actor = actor,
                        // Narrower of declaration and principal standing (#2673).
                        ActionOrigin = EndpointAuthorization.ResolveTrustedActionOrigin(context, request.ActionOrigin),
                        ControllerRole = controllerRole
                    },
                    actor,
                    scope.TenantContext.TenantId,
                    scope.TenantContext.CompanyId,
                    context.RequestAborted)
                .ConfigureAwait(false);
            if (result is null)
                return Results.NotFound(new { error = $"Close workflow '{request.WorkflowId}' was not found." });
            if (!operationsEnvelope)
                return Results.Json(result, jsonOptions);
            if (result.IsLocked && result.Transition is { Success: true } transition)
                return Results.Json(transition, jsonOptions);
            var blockers = result.Issues.Select(issue => new OperationsWorkflowBlockerDto(
                issue.Code, issue.Message, OperationsGateKeyDto.Approval, issue.Severity.ToString(), [])).ToArray();
            return Results.Json(new OperationsTransitionResultDto(false, "ACCOUNTING_CLOSE_REQUIRED",
                "The accounting period could not be closed. Resolve the shared close-plan blockers and retry.",
                result.Transition?.Workflow, blockers, []), jsonOptions, statusCode: StatusCodes.Status409Conflict);
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = [ex.Message]
            });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

}
