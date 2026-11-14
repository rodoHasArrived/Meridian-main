using System.Text.Json;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Execution.Services;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class ExecutionEndpoints
{
    private static void MapPaperSessionEndpoints(RouteGroupBuilder group, JsonSerializerOptions jsonOptions)
    {
        group.MapPost("/sessions/{sessionId}/financing", async (string sessionId, HttpContext context) =>
        {
            if (!TryResolveActor(context, out var actor))
                return Results.Unauthorized();
            if (EndpointAuthorization.ResolveTrustedActionOrigin(context, OperationsActionOriginDto.HumanOperator)
                != OperationsActionOriginDto.HumanOperator)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var persistence = context.RequestServices.GetService<PaperSessionPersistenceService>();
            if (persistence is null || context.RequestServices.GetService<PaperFinancingPostingService>() is null)
                return Results.Problem("Durable financing posting is not configured.", statusCode: 503);
            try
            {
                var request = await JsonSerializer.DeserializeAsync(context.Request.Body,
                    PaperFinancingApiJsonContext.Default.PaperFinancingRequest, context.RequestAborted).ConfigureAwait(false);
                if (request is null)
                    return Results.BadRequest();
                await persistence.RecordFinancingAsync(new PaperFinancingCharge(sessionId,
                    PaperTradingPortfolio.DefaultAccountId, request.AccrualDate, request.Amount,
                    request.SourceReference, request.Rationale, actor), context.RequestAborted).ConfigureAwait(false);
                return Results.NoContent();
            }
            catch (JsonException) { return Results.BadRequest(); }
            catch (ArgumentException ex) { return Results.Problem(ex.Message, statusCode: 400); }
            catch (InvalidDataException ex) { return Results.Problem(ex.Message, statusCode: 409); }
            catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: 409); }
            catch (Meridian.Ledger.LedgerValidationException ex) { return Results.Problem(ex.Message, statusCode: 409); }
            catch (IOException)
            {
                return Results.Problem("Financing posting is unavailable. Retry the same source assessment after recovery.", statusCode: 503);
            }
        })
        .WithName("PostPaperSessionFinancing")
        .RequirePermission(UserPermission.AdminMaintenance)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces(204).Produces(400).Produces(401).Produces(403).Produces(409).Produces(503);

        group.MapGet("/sessions", async (HttpContext context) =>
        {
            var persistence = context.RequestServices.GetService<PaperSessionPersistenceService>();
            if (persistence is null)
                return Results.Json(Array.Empty<PaperSessionSummaryDto>(), jsonOptions);

            await persistence.InitialiseAsync(context.RequestAborted).ConfigureAwait(false);
            var sessions = persistence.GetSessions();
            return Results.Json(sessions, jsonOptions);
        })
        .WithName("GetExecutionSessions").RequirePermission(UserPermission.ViewTrades)
        .Produces<IReadOnlyList<PaperSessionSummaryDto>>(200);

        group.MapGet("/sessions/{sessionId}", async (string sessionId, HttpContext context) =>
        {
            var persistence = context.RequestServices.GetService<PaperSessionPersistenceService>();
            if (persistence is null)
                return Results.NotFound();

            await persistence.InitialiseAsync(context.RequestAborted).ConfigureAwait(false);
            var session = persistence.GetSession(sessionId);
            return session is null ? Results.NotFound() : Results.Json(session, jsonOptions);
        })
        .WithName("GetExecutionSessionById").RequirePermission(UserPermission.ViewTrades)
        .Produces<PaperSessionDetailDto>(200)
        .Produces(404);

        group.MapGet("/sessions/{sessionId}/tca", async (string sessionId, HttpContext context) =>
        {
            var persistence = context.RequestServices.GetService<PaperSessionPersistenceService>();
            if (persistence is null)
                return Results.NotFound();

            await persistence.InitialiseAsync(context.RequestAborted).ConfigureAwait(false);
            var session = persistence.GetSession(sessionId);
            if (session is null)
                return Results.NotFound();

            var report = SessionTcaReporter.Generate(
                sessionId,
                session.Summary.StrategyId,
                session.FillHistory ?? Array.Empty<ExecutionReport>(),
                session.OrderHistory);
            return Results.Json(report, jsonOptions);
        })
        .WithName("GetExecutionSessionTcaReport").RequirePermission(UserPermission.ViewTrades)
        .Produces<SessionTcaReport>(200)
        .Produces(404);

        group.MapPost("/sessions/create", async (CreatePaperSessionRequest request, HttpContext context) =>
        {
            if (!HasExecutionTradingPermission(context, UserPermission.ExecuteTrades))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveActor(context, out _))
            {
                return Results.Unauthorized();
            }

            var persistence = context.RequestServices.GetService<PaperSessionPersistenceService>();
            if (persistence is null)
                return Results.Problem("Paper session management is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var actionId = GenerateActionId();
            var dto = new Meridian.Execution.Services.CreatePaperSessionDto(
                request.StrategyId,
                request.StrategyName,
                request.InitialCash,
                request.Symbols);
            PaperSessionSummaryDto session;
            try
            {
                session = await persistence.CreateSessionAsync(dto, context.RequestAborted).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    new TradingActionResult(
                        ActionId: actionId,
                        Status: "Rejected",
                        Message: ex.Message,
                        OccurredAt: DateTimeOffset.UtcNow),
                    jsonOptions,
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            await RecordOperatorAuditAsync(
                context,
                actionId,
                action: "CreatePaperSession",
                outcome: "Completed",
                message: $"Paper session {session.SessionId} created for strategy {session.StrategyId}.",
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sessionId"] = session.SessionId,
                    ["strategyId"] = session.StrategyId,
                    ["initialCash"] = session.InitialCash.ToString("G29"),
                    ["symbolCount"] = (request.Symbols?.Count ?? 0).ToString(),
                    ["symbols"] = request.Symbols is { Count: > 0 }
                        ? string.Join(",", request.Symbols)
                        : string.Empty
                }).ConfigureAwait(false);

            return Results.Json(session, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateExecutionSession").RequirePermission(UserPermission.ExecuteTrades)
        .Produces<PaperSessionSummaryDto>(201)
        .Produces(400)
        .Produces(429)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces(503);

        group.MapPost("/sessions/{sessionId}/close", async (string sessionId, HttpContext context) =>
        {
            if (!HasExecutionTradingPermission(context, UserPermission.ManageOrders))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveActor(context, out _))
            {
                return Results.Unauthorized();
            }

            var persistence = context.RequestServices.GetService<PaperSessionPersistenceService>();
            if (persistence is null)
                return Results.Problem("Paper session management is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);

            await persistence.InitialiseAsync(context.RequestAborted).ConfigureAwait(false);
            var actionId = GenerateActionId();
            var existingSession = persistence.GetSession(sessionId);
            var closed = await persistence.CloseSessionAsync(sessionId, context.RequestAborted).ConfigureAwait(false);

            var auditEntry = await RecordOperatorAuditAsync(
                context,
                actionId,
                action: "ClosePaperSession",
                outcome: closed ? "Completed" : "Rejected",
                message: closed
                    ? $"Paper session {sessionId} closed."
                    : $"Paper session {sessionId} was not found.",
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sessionId"] = sessionId,
                    ["strategyId"] = existingSession?.Summary.StrategyId ?? string.Empty,
                    ["symbolCount"] = existingSession?.Symbols.Count.ToString() ?? "0"
                }).ConfigureAwait(false);

            if (!closed)
            {
                return Results.NotFound();
            }

            return Results.Json(
                new TradingActionResult(
                    ActionId: actionId,
                    Status: "Completed",
                    Message: $"Paper session {sessionId} closed.",
                    OccurredAt: DateTimeOffset.UtcNow,
                    AuditId: auditEntry?.AuditId),
                jsonOptions);
        })
        .WithName("CloseExecutionSession").RequirePermission(UserPermission.ManageOrders)
        .Produces<TradingActionResult>(200)
        .Produces(404)
        .Produces(503);

        group.MapGet("/sessions/{sessionId}/replay", async (string sessionId, HttpContext context) =>
        {
            if (!HasExecutionTradingPermission(context, UserPermission.ManageOrders))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveActor(context, out _))
            {
                return Results.Unauthorized();
            }

            var persistence = context.RequestServices.GetService<PaperSessionPersistenceService>();
            if (persistence is null)
                return Results.Problem("Paper session management is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);

            await persistence.InitialiseAsync(context.RequestAborted).ConfigureAwait(false);
            var actionId = GenerateActionId();
            var verification = await persistence.VerifyReplayAsync(sessionId, context.RequestAborted).ConfigureAwait(false);
            if (verification is null)
            {
                await RecordOperatorAuditAsync(
                    context,
                    actionId,
                    action: "ReplayPaperSession",
                    outcome: "Rejected",
                    message: $"Paper session {sessionId} was not found for replay verification.",
                    metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["sessionId"] = sessionId
                    }).ConfigureAwait(false);

                return Results.NotFound();
            }

            var primaryMismatchReason = verification.MismatchReasons.FirstOrDefault();
            var auditEntry = await RecordOperatorAuditAsync(
                context,
                actionId,
                action: "ReplayPaperSession",
                outcome: verification.IsConsistent ? "Completed" : "AttentionRequired",
                message: verification.IsConsistent
                    ? $"Replay matched current state for paper session {sessionId}."
                    : $"Replay mismatch detected for paper session {sessionId}: {primaryMismatchReason ?? "see mismatch count"}.",
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sessionId"] = sessionId,
                    ["strategyId"] = verification.Summary.StrategyId,
                    ["isConsistent"] = verification.IsConsistent.ToString(),
                    ["replaySource"] = verification.ReplaySource,
                    ["mismatchCount"] = verification.MismatchReasons.Count.ToString(),
                    ["comparedFillCount"] = verification.ComparedFillCount.ToString(),
                    ["comparedOrderCount"] = verification.ComparedOrderCount.ToString(),
                    ["comparedLedgerEntryCount"] = verification.ComparedLedgerEntryCount.ToString(),
                    ["lastPersistedFillAt"] = verification.LastPersistedFillAt?.ToString("O") ?? string.Empty,
                    ["lastPersistedOrderUpdateAt"] = verification.LastPersistedOrderUpdateAt?.ToString("O") ?? string.Empty,
                    ["primaryMismatchReason"] = primaryMismatchReason ?? string.Empty
                }).ConfigureAwait(false);

            return Results.Json(
                verification with
                {
                    VerificationAuditId = auditEntry?.AuditId
                },
                jsonOptions);
        })
        .WithName("ReplayExecutionSession").RequirePermission(UserPermission.ManageOrders)
        .Produces<PaperSessionReplayVerificationDto>(200)
        .Produces(404)
        .Produces(503);
    }
}
