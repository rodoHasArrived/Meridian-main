using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Identity.Auth;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// REST endpoints for querying and controlling the lifecycle of registered live strategies.
/// Exposes pause, stop, and status operations. Strategy start is handled separately via
/// the execution cockpit once an execution context is established.
/// </summary>
public static class StrategyLifecycleEndpoints
{
    public static void MapStrategyLifecycleEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/strategies").WithTags("Strategies");

        // --- Status queries ---

        group.MapGet("/status", (HttpContext context) =>
        {
            var manager = context.RequestServices.GetService<StrategyLifecycleManager>();
            if (manager is null)
                return Results.Problem("Strategy lifecycle manager is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var statuses = manager.GetStatuses();
            var result = statuses.Select(kvp => new StrategyStatusDto(kvp.Key, kvp.Value.ToString())).ToArray();
            return Results.Json(result, jsonOptions);
        })
        .WithName("GetAllStrategyStatuses")
        .Produces<StrategyStatusDto[]>(200)
        .Produces(503);

        group.MapGet("/{strategyId}/status", (string strategyId, HttpContext context) =>
        {
            var manager = context.RequestServices.GetService<StrategyLifecycleManager>();
            if (manager is null)
                return Results.Problem("Strategy lifecycle manager is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var statuses = manager.GetStatuses();
            if (!statuses.TryGetValue(strategyId, out var status))
                return Results.NotFound(new { strategyId, error = "Strategy not registered." });

            return Results.Json(new StrategyStatusDto(strategyId, status.ToString()), jsonOptions);
        })
        .WithName("GetStrategyStatus")
        .Produces<StrategyStatusDto>(200)
        .Produces(404)
        .Produces(503);

        // --- Lifecycle control ---

        group.MapPost("/{strategyId}/pause", async (string strategyId, HttpContext context) =>
        {
            var manager = context.RequestServices.GetService<StrategyLifecycleManager>();
            if (manager is null)
                return ToEndpointFailureResult(
                    strategyId,
                    "pause",
                    OperationTerminalState.Blocked,
                    "Strategy lifecycle manager is not active.",
                    exception: null,
                    externalStateMayHaveChanged: false,
                    StatusCodes.Status503ServiceUnavailable,
                    jsonOptions);

            if (!manager.GetStatuses().ContainsKey(strategyId))
                return ToEndpointFailureResult(
                    strategyId,
                    "pause",
                    OperationTerminalState.Blocked,
                    "Strategy is not registered.",
                    exception: null,
                    externalStateMayHaveChanged: false,
                    StatusCodes.Status404NotFound,
                    jsonOptions);

            try
            {
                var outcome = await manager.PauseAsync(strategyId, context.RequestAborted).ConfigureAwait(false);
                return ToOperationResult(outcome, jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return ToEndpointFailureResult(
                    strategyId,
                    "pause",
                    OperationTerminalState.Blocked,
                    ex.Message,
                    ex,
                    externalStateMayHaveChanged: false,
                    StatusCodes.Status409Conflict,
                    jsonOptions);
            }
            catch (OperationCanceledException ex)
            {
                return ToEndpointFailureResult(
                    strategyId,
                    "pause",
                    OperationTerminalState.Blocked,
                    "The pause request was cancelled before a retained terminal receipt was returned.",
                    ex,
                    externalStateMayHaveChanged: true,
                    StatusCodes.Status423Locked,
                    jsonOptions);
            }
            catch (Exception ex)
            {
                return ToEndpointFailureResult(
                    strategyId,
                    "pause",
                    OperationTerminalState.Failed,
                    "The pause request failed before a retained terminal receipt was returned.",
                    ex,
                    externalStateMayHaveChanged: true,
                    StatusCodes.Status500InternalServerError,
                    jsonOptions);
            }
        })
        .WithName("PauseStrategy")
        .Produces<VerifiedOperationOutcome>(200)
        .Produces<VerifiedOperationOutcome>(409)
        .Produces<VerifiedOperationOutcome>(423)
        .Produces<VerifiedOperationOutcome>(404)
        .Produces<VerifiedOperationOutcome>(500)
        .Produces<VerifiedOperationOutcome>(503)
        .Produces(404)
        .Produces(503)
        .RequirePermission(UserPermission.ManageStrategies)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost("/{strategyId}/stop", async (string strategyId, HttpContext context) =>
        {
            var manager = context.RequestServices.GetService<StrategyLifecycleManager>();
            if (manager is null)
                return ToEndpointFailureResult(
                    strategyId,
                    "stop",
                    OperationTerminalState.Blocked,
                    "Strategy lifecycle manager is not active.",
                    exception: null,
                    externalStateMayHaveChanged: false,
                    StatusCodes.Status503ServiceUnavailable,
                    jsonOptions);

            if (!manager.GetStatuses().ContainsKey(strategyId))
                return ToEndpointFailureResult(
                    strategyId,
                    "stop",
                    OperationTerminalState.Blocked,
                    "Strategy is not registered.",
                    exception: null,
                    externalStateMayHaveChanged: false,
                    StatusCodes.Status404NotFound,
                    jsonOptions);

            try
            {
                var outcome = await manager.StopAsync(strategyId, context.RequestAborted).ConfigureAwait(false);
                return ToOperationResult(outcome, jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return ToEndpointFailureResult(
                    strategyId,
                    "stop",
                    OperationTerminalState.Blocked,
                    ex.Message,
                    ex,
                    externalStateMayHaveChanged: false,
                    StatusCodes.Status409Conflict,
                    jsonOptions);
            }
            catch (OperationCanceledException ex)
            {
                return ToEndpointFailureResult(
                    strategyId,
                    "stop",
                    OperationTerminalState.Blocked,
                    "The stop request was cancelled before a retained terminal receipt was returned.",
                    ex,
                    externalStateMayHaveChanged: true,
                    StatusCodes.Status423Locked,
                    jsonOptions);
            }
            catch (Exception ex)
            {
                return ToEndpointFailureResult(
                    strategyId,
                    "stop",
                    OperationTerminalState.Failed,
                    "The stop request failed before a retained terminal receipt was returned.",
                    ex,
                    externalStateMayHaveChanged: true,
                    StatusCodes.Status500InternalServerError,
                    jsonOptions);
            }
        })
        .WithName("StopStrategy")
        .Produces<VerifiedOperationOutcome>(200)
        .Produces<VerifiedOperationOutcome>(409)
        .Produces<VerifiedOperationOutcome>(423)
        .Produces<VerifiedOperationOutcome>(404)
        .Produces<VerifiedOperationOutcome>(500)
        .Produces<VerifiedOperationOutcome>(503)
        .Produces(404)
        .Produces(503)
        .RequirePermission(UserPermission.ManageStrategies)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost("/{strategyId}/reconcile", async (string strategyId, HttpContext context) =>
        {
            var manager = context.RequestServices.GetService<StrategyLifecycleManager>();
            if (manager is null)
                return ToEndpointFailureResult(
                    strategyId,
                    "reconcile",
                    OperationTerminalState.Blocked,
                    "Strategy lifecycle manager is not active.",
                    exception: null,
                    externalStateMayHaveChanged: false,
                    StatusCodes.Status503ServiceUnavailable,
                    jsonOptions);

            if (!manager.GetStatuses().ContainsKey(strategyId))
                return ToEndpointFailureResult(
                    strategyId,
                    "reconcile",
                    OperationTerminalState.Blocked,
                    "Strategy is not registered.",
                    exception: null,
                    externalStateMayHaveChanged: false,
                    StatusCodes.Status404NotFound,
                    jsonOptions);

            try
            {
                var outcome = await manager
                    .ReconcileExternalStateAsync(strategyId, context.RequestAborted)
                    .ConfigureAwait(false);
                return ToOperationResult(outcome, jsonOptions);
            }
            catch (InvalidOperationException ex)
            {
                return ToEndpointFailureResult(
                    strategyId,
                    "reconcile",
                    OperationTerminalState.Blocked,
                    ex.Message,
                    ex,
                    externalStateMayHaveChanged: false,
                    StatusCodes.Status409Conflict,
                    jsonOptions);
            }
            catch (OperationCanceledException ex)
            {
                return ToEndpointFailureResult(
                    strategyId,
                    "reconcile",
                    OperationTerminalState.Blocked,
                    "The reconciliation request was cancelled before a retained terminal receipt was returned.",
                    ex,
                    externalStateMayHaveChanged: true,
                    StatusCodes.Status423Locked,
                    jsonOptions);
            }
            catch (Exception ex)
            {
                return ToEndpointFailureResult(
                    strategyId,
                    "reconcile",
                    OperationTerminalState.Failed,
                    "The reconciliation request failed before a retained terminal receipt was returned.",
                    ex,
                    externalStateMayHaveChanged: true,
                    StatusCodes.Status500InternalServerError,
                    jsonOptions);
            }
        })
        .WithName("ReconcileStrategyLifecycle")
        .Produces<VerifiedOperationOutcome>(200)
        .Produces<VerifiedOperationOutcome>(409)
        .Produces<VerifiedOperationOutcome>(423)
        .Produces<VerifiedOperationOutcome>(404)
        .Produces<VerifiedOperationOutcome>(500)
        .Produces<VerifiedOperationOutcome>(503)
        .Produces(404)
        .Produces(503)
        .RequirePermission(UserPermission.ManageStrategies)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);
    }

    internal static IResult ToOperationResult(
        VerifiedOperationOutcome outcome,
        JsonSerializerOptions jsonOptions)
    {
        VerifiedOperationOutcomeValidator.ValidateAndThrow(outcome);
        var statusCode = outcome.State switch
        {
            OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings =>
                StatusCodes.Status200OK,
            OperationTerminalState.Blocked => StatusCodes.Status423Locked,
            _ => StatusCodes.Status409Conflict
        };
        return Results.Json(outcome, jsonOptions, statusCode: statusCode);
    }

    internal static IResult ToEndpointFailureResult(
        string strategyId,
        string action,
        OperationTerminalState state,
        string message,
        Exception? exception,
        bool externalStateMayHaveChanged,
        int statusCode,
        JsonSerializerOptions jsonOptions) =>
        Results.Json(
            CreateEndpointFailureOutcome(
                strategyId,
                action,
                state,
                message,
                exception,
                externalStateMayHaveChanged),
            jsonOptions,
            statusCode: statusCode);

    internal static VerifiedOperationOutcome CreateEndpointFailureOutcome(
        string strategyId,
        string action,
        OperationTerminalState state,
        string message,
        Exception? exception,
        bool externalStateMayHaveChanged)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (state is not (OperationTerminalState.Failed or OperationTerminalState.Blocked))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "Endpoint failure receipts must be Failed or Blocked.");
        }

        var now = DateTimeOffset.UtcNow;
        var operationId = $"strategy-lifecycle:{action}:{strategyId}:{Guid.NewGuid():N}";
        var inputHash = HashText($"{strategyId}|{action}");
        var evidenceText = $"{operationId}|{state}|{exception?.GetType().FullName}|{message}";
        var evidence = new OperationEvidenceReference(
            $"{operationId}:endpoint-failure",
            "endpoint-failure",
            message,
            ContentHashSha256: HashText(evidenceText),
            CapturedAtUtc: now);
        var recovery = externalStateMayHaveChanged
            ? new OperationRecoveryAction(
                "reconcile-before-retry",
                "Reconcile before retry",
                "Do not repeat the lifecycle command. Inspect the strategy's external state and use reconciliation " +
                "to retain the observed result before deciding whether another command is safe.",
                Retryable: false,
                RequiresHumanAction: true,
                Route: $"/api/strategies/{Uri.EscapeDataString(strategyId)}/reconcile")
            : new OperationRecoveryAction(
                "resolve-prerequisite",
                "Resolve prerequisite",
                "Resolve the reported lifecycle prerequisite, refresh strategy status, and then retry the command.",
                Retryable: true,
                RequiresHumanAction: true,
                Route: "/strategy");
        recovery = recovery with { EvidenceIds = [evidence.EvidenceId] };

        return VerifiedOperationOutcomeValidator.ValidateAndThrow(new VerifiedOperationOutcome(
            operationId,
            $"strategy-lifecycle.{action}",
            state,
            now,
            now,
            1,
            operationId,
            inputHash,
            [new OperationPostcondition(
                "lifecycle-effect-confirmed",
                $"The {action} lifecycle effect was confirmed.",
                OperationPostconditionState.NotSatisfied,
                Required: true,
                EvidenceIds: [evidence.EvidenceId])],
            [evidence],
            [],
            [new OperationIssue(
                externalStateMayHaveChanged ? "external-state-requires-reconciliation" : "lifecycle-prerequisite",
                exception is null ? message : $"{message} {exception.Message}",
                OperationIssueSeverity.Error,
                exception?.GetType().FullName,
                evidence.EvidenceId)
            {
                IsBlocking = state == OperationTerminalState.Blocked
            }],
            [recovery]));
    }

    private static string HashText(string value) =>
        Sha256Digest.ComputeUtf8(value);
}

// --- DTOs ---

/// <summary>Snapshot of a strategy's current lifecycle state.</summary>
public sealed record StrategyStatusDto(string StrategyId, string Status);

/// <summary>Result of a lifecycle control action (pause/stop).</summary>
public sealed record StrategyActionResult(
    string StrategyId,
    string Action,
    bool Success,
    string? Reason);
