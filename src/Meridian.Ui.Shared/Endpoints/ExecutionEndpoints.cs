using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// REST endpoints for the paper-trading cockpit and execution dashboard.
/// Exposes positions, orders, portfolio state, and gateway controls.
/// </summary>
public static class ExecutionEndpoints
{
    public static void MapExecutionEndpoints(this WebApplication app, JsonSerializerOptions jsonOptions)
    {
        var group = app.MapGroup("/api/execution").WithTags("Execution");

        // --- Portfolio / Account ---

        group.MapGet("/account", (HttpContext context) =>
        {
            var portfolio = context.RequestServices.GetService<IPortfolioState>();
            if (portfolio is null)
                return Results.Problem("Paper trading portfolio is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var snapshot = new ExecutionAccountSnapshot(
                Cash: portfolio.Cash,
                PortfolioValue: portfolio.PortfolioValue,
                UnrealisedPnl: portfolio.UnrealisedPnl,
                RealisedPnl: portfolio.RealisedPnl,
                PositionCount: portfolio.Positions.Count,
                AsOf: DateTimeOffset.UtcNow);

            return Results.Json(snapshot, jsonOptions);
        })
        .WithName("GetExecutionAccount")
        .Produces<ExecutionAccountSnapshot>(200)
        .Produces(503);

        group.MapGet("/positions", (HttpContext context) =>
        {
            var portfolio = context.RequestServices.GetService<IPortfolioState>();
            if (portfolio is null)
                return Results.Problem("Paper trading portfolio is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var positions = portfolio.Positions.Values.ToArray();
            return Results.Json(positions, jsonOptions);
        })
        .WithName("GetExecutionPositions")
        .Produces<ExecutionPosition[]>(200)
        .Produces(503);

        group.MapGet("/positions/blotter", async (HttpContext context) =>
        {
            var snapshot = await BuildBlotterSnapshotAsync(
                context.RequestServices,
                context.RequestAborted).ConfigureAwait(false);

            return snapshot is null
                ? Results.Problem("Execution position services are not active.", statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Json(snapshot, jsonOptions);
        })
        .WithName("GetExecutionBlotterPositions")
        .Produces<ExecutionBlotterSnapshotResponse>(200)
        .Produces(503);

        group.MapGet("/portfolio", (HttpContext context) =>
        {
            var portfolio = context.RequestServices.GetService<IPortfolioState>();
            if (portfolio is null)
                return Results.Problem("Paper trading portfolio is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var snapshot = new ExecutionPortfolioSnapshot(
                Cash: portfolio.Cash,
                PortfolioValue: portfolio.PortfolioValue,
                UnrealisedPnl: portfolio.UnrealisedPnl,
                RealisedPnl: portfolio.RealisedPnl,
                Positions: portfolio.Positions.Values.Cast<ExecutionPosition>().ToArray(),
                AsOf: DateTimeOffset.UtcNow);

            return Results.Json(snapshot, jsonOptions);
        })
        .WithName("GetExecutionPortfolio")
        .Produces<ExecutionPortfolioSnapshot>(200)
        .Produces(503);

        // --- Orders ---

        group.MapGet("/orders", (HttpContext context) =>
        {
            var oms = context.RequestServices.GetService<IOrderManager>();
            if (oms is null)
                return Results.Problem("Order management system is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var orders = oms.GetOpenOrders();
            return Results.Json(orders, jsonOptions);
        })
        .WithName("GetOpenOrders")
        .Produces<IReadOnlyList<OrderState>>(200)
        .Produces(503);

        group.MapGet("/orders/{orderId}", (string orderId, HttpContext context) =>
        {
            var oms = context.RequestServices.GetService<IOrderManager>();
            if (oms is null)
                return Results.Problem("Order management system is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var order = oms.GetOrder(orderId);
            return order is null
                ? Results.NotFound()
                : Results.Json(order, jsonOptions);
        })
        .WithName("GetOrderById")
        .Produces<OrderState>(200)
        .Produces(404)
        .Produces(503);

        group.MapPost("/orders/submit", async (OrderRequest request, HttpContext context) =>
        {
            if (!HasExecutionTradingPermission(context, UserPermission.ManageOrders))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var oms = context.RequestServices.GetService<IOrderManager>();
            if (oms is null)
                return Results.Problem("Order management system is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var activeGatewayId = context.RequestServices.GetService<IExecutionGateway>()?.GatewayId;
            var gateDecision = BrokerageOrderPlacementGate.Evaluate(
                context.RequestServices.GetService<BrokerageConfiguration>(),
                activeGatewayId);
            if (!gateDecision.IsAllowed)
            {
                var blocked = new OrderResult
                {
                    Success = false,
                    OrderId = request.ClientOrderId ?? "blocked",
                    ErrorMessage = gateDecision.RejectReason ?? "Broker order routing is disabled by validation gates."
                };
                return Results.Json(blocked, jsonOptions, statusCode: StatusCodes.Status403Forbidden);
            }

            if (TryRejectClientControlledExecutionMetadata(request, jsonOptions) is { } brokerAccountFailure)
            {
                return brokerAccountFailure;
            }

            if (request.FundAccountId is { } fundAccountId
                && await RequireExecutionFundAccountAccessAsync(
                    fundAccountId,
                    UserPermission.ManageOrders,
                    context,
                    jsonOptions).ConfigureAwait(false) is { } accountScopeFailure)
            {
                return accountScopeFailure;
            }

            string? correlationId = null;
            request.Metadata?.TryGetValue("correlationId", out correlationId);
            var normalizedRequest = request with
            {
                Metadata = MergeMetadata(
                    request.Metadata,
                    ("actor", actor),
                    ("correlationId", string.IsNullOrWhiteSpace(correlationId) ? GenerateActionId() : correlationId))
            };

            var result = await oms.PlaceOrderAsync(normalizedRequest, context.RequestAborted).ConfigureAwait(false);

            // A parked order is not a rejection: nothing routed, but a live queue entry can
            // still execute it once an operator approves. 202 keeps that distinguishable
            // from the 400 a refusal returns, so a client cannot show "submission failed"
            // for an order that is on its way to the desk.
            return (result.Success, result.RequiresApproval) switch
            {
                (true, _) => Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created),
                (false, true) => Results.Json(result, jsonOptions, statusCode: StatusCodes.Status202Accepted),
                _ => Results.Json(result, jsonOptions, statusCode: StatusCodes.Status400BadRequest)
            };
        })
        .WithName("SubmitOrder")
        .Produces<OrderResult>(201)
        .Produces<OrderResult>(202)
        .Produces<OrderResult>(400)
        .Produces<OrderResult>(403)
        .Produces(429)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces(503);

        group.MapPost("/orders/{orderId}/cancel", async (string orderId, HttpContext context) =>
        {
            if (!HasExecutionTradingPermission(context, UserPermission.ManageOrders))
            {
                return EndpointHelpers.Forbidden();
            }

            var oms = context.RequestServices.GetService<IOrderManager>();
            if (oms is null)
                return Results.Problem("Order management system is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            // Cancelling a parked order durably withdraws its governed approval, which the
            // escalation routes only allow within the caller's scoped authority over the
            // owning fund. Reaching the same withdrawal through a client order id must not
            // be the cheaper path: without this, an operator holding ManageOrders for one
            // fund could retire another fund's approval just by knowing its id.
            if (oms.GetOrder(orderId)?.FundAccountId is { } scopedFundAccountId &&
                !EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance) &&
                !await EndpointAuthorization.HasScopedPermissionAsync(
                    context,
                    UserPermission.ManageOrders,
                    AccessScopeKindDto.Account,
                    scopedFundAccountId,
                    context.RequestAborted).ConfigureAwait(false))
            {
                return EndpointHelpers.Forbidden();
            }

            var logger = GetLogger(context.RequestServices);
            var actionId = GenerateActionId();
            var result = await oms.CancelOrderAsync(orderId, context.RequestAborted).ConfigureAwait(false);

            if (result.Success)
            {
                logger.LogInformation("Trading action {ActionId}: cancel order {OrderId} — succeeded", actionId, orderId);
            }
            else
            {
                logger.LogWarning("Trading action {ActionId}: cancel order {OrderId} — rejected: {Reason}", actionId, orderId, result.ErrorMessage);
            }

            var actionResult = new TradingActionResult(
                ActionId: actionId,
                Status: result.Success ? "Completed" : "Rejected",
                Message: result.Success ? $"Order {orderId} cancelled." : (result.ErrorMessage ?? "Cancel rejected."),
                OccurredAt: DateTimeOffset.UtcNow);

            return result.Success
                ? Results.Json(actionResult, jsonOptions)
                : Results.Json(actionResult, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
        })
        .WithName("CancelOrder")
        .Produces<TradingActionResult>(200)
        .Produces<TradingActionResult>(400)
        .Produces(503);

        group.MapPost("/orders/cancel-all", async (HttpContext context) =>
        {
            if (!HasExecutionTradingPermission(context, UserPermission.ManageOrders))
            {
                return EndpointHelpers.Forbidden();
            }

            var oms = context.RequestServices.GetService<IOrderManager>();
            if (oms is null)
                return Results.Problem("Order management system is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var logger = GetLogger(context.RequestServices);
            var actionId = GenerateActionId();
            var openCount = oms.GetOpenOrders().Count;

            var sweep = await oms.CancelAllAsync(context.RequestAborted).ConfigureAwait(false)
                ?? KillSwitchSweepResult.Unestablished(openCount);

            logger.LogInformation(
                "Trading action {ActionId}: cancel-all — cancelled {Cancelled} of {Requested} open order(s), {StillWorking} still working",
                actionId,
                sweep.Cancelled,
                sweep.Requested,
                sweep.StillWorking.Count);

            // The operator is told what the sweep achieved, not that it was requested. A ticket
            // reading "Completed" over a book that still has working orders is the specific
            // failure this endpoint used to produce.
            var actionResult = new TradingActionResult(
                ActionId: actionId,
                Status: sweep.Outcome.ToString(),
                Message: sweep.Describe(),
                OccurredAt: DateTimeOffset.UtcNow);

            return Results.Json(actionResult, jsonOptions);
        })
        .WithName("CancelAllOrders")
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<TradingActionResult>(200)
        .Produces(403)
        .Produces(429)
        .Produces(503);

        // --- Gateway health & capabilities ---

        group.MapGet("/health", (HttpContext context) =>
        {
            var gateway = context.RequestServices.GetService<IOrderGateway>();
            var executionGateway = context.RequestServices.GetService<IExecutionGateway>();
            if (gateway is null)
                return Results.Problem("No execution gateway is configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var health = new ExecutionGatewayHealth(
                BrokerName: gateway.BrokerName,
                Mode: gateway.Mode.ToString(),
                IsAvailable: true,
                AsOf: DateTimeOffset.UtcNow,
                SelectedGatewayId: executionGateway?.GatewayId);

            return Results.Json(health, jsonOptions);
        })
        .WithName("GetExecutionHealth")
        .Produces<ExecutionGatewayHealth>(200)
        .Produces(503);

        group.MapGet("/capabilities", (HttpContext context) =>
        {
            var gateway = context.RequestServices.GetService<IOrderGateway>();
            if (gateway is null)
                return Results.Problem("No execution gateway is configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

            return Results.Json(gateway.Capabilities, jsonOptions);
        })
        .WithName("GetExecutionCapabilities")
        .Produces<OrderGatewayCapabilities>(200)
        .Produces(503);

        group.MapGet("/audit", async (int? take, HttpContext context) =>
        {
            var auditTrail = context.RequestServices.GetService<ExecutionAuditTrailService>();
            if (auditTrail is null)
            {
                return Results.Json(Array.Empty<ExecutionAuditEntry>(), jsonOptions);
            }

            var entries = await auditTrail
                .GetRecentAsync(take ?? 100, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(entries, jsonOptions);
        })
        .WithName("GetExecutionAudit")
        .Produces<IReadOnlyList<ExecutionAuditEntry>>(200);

        group.MapGet("/audit/search", async (
            string? searchText,
            string? runId,
            string? category,
            string? action,
            string? outcome,
            string? actor,
            string? symbol,
            string? correlationId,
            string? objectKind,
            string? objectId,
            string? relatedObjectId,
            DateTimeOffset? fromUtc,
            DateTimeOffset? toUtc,
            int? limit,
            HttpContext context) =>
        {
            var explorer = context.RequestServices.GetService<AuditTrailExplorerService>();
            if (explorer is null)
            {
                var auditTrail = context.RequestServices.GetService<ExecutionAuditTrailService>();
                explorer = auditTrail is null
                    ? new AuditTrailExplorerService()
                    : new AuditTrailExplorerService(auditTrail);
            }

            var result = await explorer.SearchAsync(
                new AuditTrailExplorerQueryDto(
                    SearchText: searchText,
                    RunId: runId,
                    Category: category,
                    Action: action,
                    Outcome: outcome,
                    Actor: actor,
                    Symbol: symbol,
                    CorrelationId: correlationId,
                    ObjectKind: objectKind,
                    ObjectId: objectId,
                    RelatedObjectId: relatedObjectId,
                    FromUtc: fromUtc,
                    ToUtc: toUtc,
                    Limit: limit ?? 100),
                context.RequestAborted).ConfigureAwait(false);

            return Results.Json(result, jsonOptions);
        })
        .WithName("SearchExecutionAuditTrail")
        .Produces<AuditTrailExplorerResultDto>(200);

        group.MapGet("/controls", (HttpContext context) =>
        {
            var controls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            if (controls is null)
            {
                return Results.Problem("Execution operator controls are not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Json(controls.GetSnapshot(), jsonOptions);
        })
        .WithName("GetExecutionControls")
        .Produces<ExecutionControlSnapshot>(200)
        .Produces(503);

        group.MapPost("/controls/circuit-breaker", async (UpdateExecutionCircuitBreakerRequest request, HttpContext context) =>
        {
            if (!HasExecutionControlMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var controls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            if (controls is null)
            {
                return Results.Problem("Execution operator controls are not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var snapshot = await controls
                .SetCircuitBreakerAsync(request.IsOpen, request.Reason, actor, request.CorrelationId, context.RequestAborted)
                .ConfigureAwait(false);

            // Opening the breaker is the kill switch: beyond blocking new submissions it must
            // sweep the open book, or resting orders keep filling while routing is "halted".
            // The cancel sweep runs after the durable breaker flip so a crash between the two
            // restarts into the halted state, and its outcome is audited separately from the
            // activation so a failed sweep is visible rather than silently absorbed.
            if (request.IsOpen && context.RequestServices.GetService<IOrderManager>() is { } oms)
            {
                var auditTrail = context.RequestServices.GetService<ExecutionAuditTrailService>();
                var openCount = oms.GetOpenOrders().Count;
                try
                {
                    // A null sweep is an order manager that established nothing about the book.
                    // Fail closed on it rather than dereferencing: the kill switch reporting
                    // "object reference not set" tells an operator nothing about their orders.
                    var sweep = await oms.CancelAllAsync(context.RequestAborted).ConfigureAwait(false)
                        ?? KillSwitchSweepResult.Unestablished(openCount);

                    // Outcome, not invocation. The Failed branch below fires only on a thrown
                    // exception, so a broker that merely refuses a cancellation never reaches it —
                    // which is how a half-fired kill switch used to be audited as Completed.
                    if (sweep.RequiresOperatorAction)
                    {
                        GetLogger(context.RequestServices).LogError(
                            "Circuit breaker opened by {Actor} but the cancel-all sweep left {StillWorking} order(s) working; manual cancellation is required",
                            actor,
                            sweep.StillWorking.Count);
                    }
                    else
                    {
                        GetLogger(context.RequestServices).LogInformation(
                            "Circuit breaker opened by {Actor}; cancel-all emptied the book of {Count} open order(s)",
                            actor, sweep.Requested);
                    }

                    if (auditTrail is not null)
                    {
                        await auditTrail.RecordAsync(
                                "controls",
                                "CircuitBreakerCancelAll",
                                sweep.Outcome.ToString(),
                                actor: actor,
                                correlationId: request.CorrelationId,
                                message: sweep.Describe(),
                                ct: CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    GetLogger(context.RequestServices).LogError(
                        ex,
                        "Circuit breaker opened by {Actor} but the cancel-all sweep failed; open orders may remain working",
                        actor);
                    if (auditTrail is not null)
                    {
                        await auditTrail.RecordAsync(
                                "controls",
                                "CircuitBreakerCancelAll",
                                "Failed",
                                actor: actor,
                                correlationId: request.CorrelationId,
                                message: $"Kill-switch cancel-all failed with {openCount} open order(s); manual cancellation is required.",
                                reason: ex.Message,
                                ct: CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
            }

            return Results.Json(snapshot, jsonOptions);
        })
        .WithName("UpdateExecutionCircuitBreaker")
        .Produces<ExecutionControlSnapshot>(200)
        .Produces(403)
        .Produces(429)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces(503);

        group.MapPost("/controls/position-limits/default", async (UpdateExecutionDefaultPositionLimitRequest request, HttpContext context) =>
        {
            if (!HasExecutionControlMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var controls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            if (controls is null)
            {
                return Results.Problem("Execution operator controls are not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var snapshot = await controls
                .SetDefaultPositionLimitAsync(request.MaxPositionSize, actor, request.Reason, context.RequestAborted)
                .ConfigureAwait(false);

            return Results.Json(snapshot, jsonOptions);
        })
        .WithName("UpdateExecutionDefaultPositionLimit")
        .Produces<ExecutionControlSnapshot>(200)
        .Produces(403)
        .Produces(429)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces(503);

        group.MapPost("/controls/position-limits/{symbol}", async (string symbol, UpdateExecutionSymbolPositionLimitRequest request, HttpContext context) =>
        {
            if (!HasExecutionControlMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var controls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            if (controls is null)
            {
                return Results.Problem("Execution operator controls are not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var snapshot = await controls
                .SetSymbolPositionLimitAsync(symbol, request.MaxPositionSize, actor, request.Reason, context.RequestAborted)
                .ConfigureAwait(false);

            return Results.Json(snapshot, jsonOptions);
        })
        .WithName("UpdateExecutionSymbolPositionLimit")
        .Produces<ExecutionControlSnapshot>(200)
        .Produces(403)
        .Produces(429)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces(503);

        group.MapPost("/controls/manual-overrides", async (CreateExecutionManualOverrideRequest request, HttpContext context) =>
        {
            if (!HasExecutionControlMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var controls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            if (controls is null)
            {
                return Results.Problem("Execution operator controls are not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            try
            {
                var overrideEntry = await controls.CreateManualOverrideAsync(
                    new ManualOverrideRequest(
                        Kind: request.Kind,
                        Reason: request.Reason,
                        CreatedBy: actor,
                        Symbol: request.Symbol,
                        StrategyId: request.StrategyId,
                        RunId: request.RunId,
                        ExpiresAt: request.ExpiresAt,
                        CorrelationId: request.CorrelationId),
                    context.RequestAborted).ConfigureAwait(false);
                return Results.Json(overrideEntry, jsonOptions, statusCode: StatusCodes.Status201Created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("CreateExecutionManualOverride")
        .Produces<ExecutionManualOverride>(201)
        .Produces(403)
        .Produces(429)
        .Produces(400)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        group.MapPost("/controls/manual-overrides/{overrideId}/clear", async (string overrideId, ClearExecutionManualOverrideRequest request, HttpContext context) =>
        {
            if (!HasExecutionControlMutationPermission(context))
            {
                return EndpointHelpers.Forbidden();
            }

            if (!TryResolveActor(context, out var actor))
            {
                return Results.Unauthorized();
            }

            var controls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            if (controls is null)
            {
                return Results.Problem("Execution operator controls are not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var cleared = await controls.ClearManualOverrideAsync(
                overrideId,
                actor,
                request.Reason,
                request.CorrelationId,
                context.RequestAborted).ConfigureAwait(false);

            if (!cleared)
            {
                return Results.NotFound();
            }

            return Results.Json(
                new TradingActionResult(
                    ActionId: request.CorrelationId ?? GenerateActionId(),
                    Status: "Completed",
                    Message: $"Manual override {overrideId} cleared.",
                    OccurredAt: DateTimeOffset.UtcNow),
                jsonOptions);
        })
        .WithName("ClearExecutionManualOverride")
        .Produces<TradingActionResult>(200)
        .Produces(403)
        .Produces(429)
        .Produces(404)
        .Produces(503)
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy);

        // --- Session management ---

        group.MapGet("/sessions", async (HttpContext context) =>
        {
            var persistence = context.RequestServices.GetService<PaperSessionPersistenceService>();
            if (persistence is null)
                return Results.Json(Array.Empty<PaperSessionSummaryDto>(), jsonOptions);

            await persistence.InitialiseAsync(context.RequestAborted).ConfigureAwait(false);
            var sessions = persistence.GetSessions();
            return Results.Json(sessions, jsonOptions);
        })
        .WithName("GetExecutionSessions")
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
        .WithName("GetExecutionSessionById")
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
        .WithName("GetExecutionSessionTcaReport")
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
        .WithName("CreateExecutionSession")
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
        .WithName("CloseExecutionSession")
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
        .WithName("ReplayExecutionSession")
        .Produces<PaperSessionReplayVerificationDto>(200)
        .Produces(404)
        .Produces(503);

        // --- Multi-account endpoints ---

        group.MapGet("/accounts", (HttpContext context) =>
        {
            var portfolio = context.RequestServices.GetService<IPortfolioState>();
            if (portfolio is null)
                return Results.Problem("Paper trading portfolio is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            if (portfolio is IMultiAccountPortfolioState multi)
            {
                var snapshots = multi.Accounts.Select(static a => a.TakeSnapshot()).ToArray();
                return Results.Json(snapshots, jsonOptions);
            }

            // Backward-compat: wrap the single-account view as a list.
            var single = BuildLegacySingleAccountSnapshot(portfolio);
            return Results.Json(new[] { single }, jsonOptions);
        })
        .WithName("GetExecutionAccounts")
        .Produces<IReadOnlyList<ExecutionAccountDetailSnapshot>>(200)
        .Produces(503);

        group.MapGet("/accounts/{accountId}", (string accountId, HttpContext context) =>
        {
            var portfolio = context.RequestServices.GetService<IPortfolioState>();
            if (portfolio is null)
                return Results.Problem("Paper trading portfolio is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            if (portfolio is IMultiAccountPortfolioState multi)
            {
                var snapshot = multi.GetAccount(accountId)?.TakeSnapshot();
                return snapshot is null ? Results.NotFound() : Results.Json(snapshot, jsonOptions);
            }

            if (string.Equals(accountId, "default", StringComparison.OrdinalIgnoreCase))
                return Results.Json(BuildLegacySingleAccountSnapshot(portfolio), jsonOptions);

            return Results.NotFound();
        })
        .WithName("GetExecutionAccountById")
        .Produces<ExecutionAccountDetailSnapshot>(200)
        .Produces(404)
        .Produces(503);

        group.MapGet("/accounts/{accountId}/positions", (string accountId, HttpContext context) =>
        {
            var portfolio = context.RequestServices.GetService<IPortfolioState>();
            if (portfolio is null)
                return Results.Problem("Paper trading portfolio is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            if (portfolio is IMultiAccountPortfolioState multi)
            {
                var account = multi.GetAccount(accountId);
                if (account is null)
                    return Results.NotFound();
                return Results.Json(account.Positions.Values.ToArray(), jsonOptions);
            }

            if (string.Equals(accountId, "default", StringComparison.OrdinalIgnoreCase))
                return Results.Json(portfolio.Positions.Values.ToArray(), jsonOptions);

            return Results.NotFound();
        })
        .WithName("GetExecutionAccountPositions")
        .Produces<ExecutionPosition[]>(200)
        .Produces(404)
        .Produces(503);

        group.MapGet("/portfolio/aggregate", (HttpContext context) =>
        {
            var portfolio = context.RequestServices.GetService<IPortfolioState>();
            if (portfolio is null)
                return Results.Problem("Paper trading portfolio is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            if (portfolio is IMultiAccountPortfolioState multi)
                return Results.Json(multi.GetAggregateSnapshot(), jsonOptions);

            // Wrap single-account view.
            var singleSnap = BuildLegacySingleAccountSnapshot(portfolio);
            var aggregate = MultiAccountPortfolioSnapshot.FromAccounts([singleSnap]);
            return Results.Json(aggregate, jsonOptions);
        })
        .WithName("GetExecutionPortfolioAggregate")
        .Produces<MultiAccountPortfolioSnapshot>(200)
        .Produces(503);

        // --- Position actions ---

        group.MapPost("/positions/actions/close", async (ExecutionPositionActionRequest request, HttpContext context) =>
        {
            if (!HasExecutionTradingPermission(context, UserPermission.ExecuteTrades))
            {
                return EndpointHelpers.Forbidden();
            }

            var snapshot = await BuildBlotterSnapshotAsync(
                context.RequestServices,
                context.RequestAborted).ConfigureAwait(false);

            if (snapshot is null)
                return Results.Problem("Execution services are not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var position = snapshot.Positions.FirstOrDefault(p =>
                string.Equals(p.PositionKey, request.PositionKey, StringComparison.OrdinalIgnoreCase));

            if (position is null)
            {
                var notFound = new TradingActionResult(
                    ActionId: GenerateActionId(),
                    Status: "Rejected",
                    Message: $"Position {request.PositionKey} was not found.",
                    OccurredAt: DateTimeOffset.UtcNow);
                return Results.Json(notFound, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
            }

            if (await RequireScopedFundAccountOrderAccessAsync(
                    context,
                    request.FundAccountId,
                    UserPermission.ExecuteTrades).ConfigureAwait(false) is { } fundAccountFailure)
            {
                return fundAccountFailure;
            }

            return await SubmitPositionActionAsync(
                position,
                snapshot.Source,
                actionName: "ClosePosition",
                side: position.Quantity < 0 ? OrderSide.Buy : OrderSide.Sell,
                quantity: request.Quantity ?? Math.Abs(position.Quantity),
                positionEffect: position.AssetClass.Equals("option", StringComparison.OrdinalIgnoreCase) ? "close" : null,
                fundAccountId: request.FundAccountId,
                successVerb: "Close",
                jsonOptions: jsonOptions,
                context: context).ConfigureAwait(false);
        })
        .WithName("ClosePositionByKey")
        .Produces<TradingActionResult>(200)
        .Produces<TradingActionResult>(400)
        .Produces(403)
        .Produces(503);

        group.MapPost("/positions/actions/upsize", async (ExecutionPositionActionRequest request, HttpContext context) =>
        {
            if (!HasExecutionTradingPermission(context, UserPermission.ExecuteTrades))
            {
                return EndpointHelpers.Forbidden();
            }

            var snapshot = await BuildBlotterSnapshotAsync(
                context.RequestServices,
                context.RequestAborted).ConfigureAwait(false);

            if (snapshot is null)
                return Results.Problem("Execution services are not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var position = snapshot.Positions.FirstOrDefault(p =>
                string.Equals(p.PositionKey, request.PositionKey, StringComparison.OrdinalIgnoreCase));

            if (position is null)
            {
                var notFound = new TradingActionResult(
                    ActionId: GenerateActionId(),
                    Status: "Rejected",
                    Message: $"Position {request.PositionKey} was not found.",
                    OccurredAt: DateTimeOffset.UtcNow);
                return Results.Json(notFound, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
            }

            if (await RequireScopedFundAccountOrderAccessAsync(
                    context,
                    request.FundAccountId,
                    UserPermission.ExecuteTrades).ConfigureAwait(false) is { } fundAccountFailure)
            {
                return fundAccountFailure;
            }

            return await SubmitPositionActionAsync(
                position,
                snapshot.Source,
                actionName: "UpsizePosition",
                side: position.Quantity < 0 ? OrderSide.Sell : OrderSide.Buy,
                quantity: request.Quantity ?? Math.Abs(position.Quantity),
                positionEffect: position.AssetClass.Equals("option", StringComparison.OrdinalIgnoreCase) ? "open" : null,
                fundAccountId: request.FundAccountId,
                successVerb: "Upsize",
                jsonOptions: jsonOptions,
                context: context).ConfigureAwait(false);
        })
        .WithName("UpsizePositionByKey")
        .Produces<TradingActionResult>(200)
        .Produces<TradingActionResult>(400)
        .Produces(403)
        .Produces(503);

        group.MapPost("/positions/{symbol}/close", async (string symbol, Guid? fundAccountId, HttpContext context) =>
        {
            if (!HasExecutionTradingPermission(context, UserPermission.ExecuteTrades))
            {
                return EndpointHelpers.Forbidden();
            }

            var snapshot = await BuildBlotterSnapshotAsync(
                context.RequestServices,
                context.RequestAborted).ConfigureAwait(false);

            if (snapshot is null)
                return Results.Problem("Execution services are not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var symbolUpper = symbol.ToUpperInvariant();
            var matches = snapshot.Positions
                .Where(position => string.Equals(position.Symbol, symbolUpper, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length == 0)
            {
                var notFound = new TradingActionResult(
                    ActionId: GenerateActionId(),
                    Status: "Rejected",
                    Message: $"No open position found for {symbolUpper}.",
                    OccurredAt: DateTimeOffset.UtcNow);
                return Results.Json(notFound, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
            }

            if (matches.Length > 1)
            {
                var ambiguous = new TradingActionResult(
                    ActionId: GenerateActionId(),
                    Status: "Rejected",
                    Message: $"Multiple positions match {symbolUpper}. Use the keyed position action endpoint.",
                    OccurredAt: DateTimeOffset.UtcNow);
                return Results.Json(ambiguous, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
            }

            if (await RequireScopedFundAccountOrderAccessAsync(
                    context,
                    fundAccountId,
                    UserPermission.ExecuteTrades).ConfigureAwait(false) is { } fundAccountFailure)
            {
                return fundAccountFailure;
            }

            var position = matches[0];
            return await SubmitPositionActionAsync(
                position,
                snapshot.Source,
                actionName: "ClosePosition",
                side: position.Quantity < 0 ? OrderSide.Buy : OrderSide.Sell,
                quantity: Math.Abs(position.Quantity),
                positionEffect: position.AssetClass.Equals("option", StringComparison.OrdinalIgnoreCase) ? "close" : null,
                fundAccountId: fundAccountId,
                successVerb: "Close",
                jsonOptions: jsonOptions,
                context: context).ConfigureAwait(false);
        })
        .WithName("ClosePosition")
        .RequireRateLimiting(UiEndpoints.MutationRateLimitPolicy)
        .Produces<TradingActionResult>(200)
        .Produces<TradingActionResult>(400)
        .Produces(403)
        .Produces(429)
        .Produces(503);
    }

    // ------------------------------------------------------------------ //
    // Private helpers                                                     //
    // ------------------------------------------------------------------ //

    private static async Task<ExecutionBlotterSnapshotResponse?> BuildBlotterSnapshotAsync(
        IServiceProvider services,
        CancellationToken ct)
    {
        var executionGateway = services.GetService<IExecutionGateway>();
        var orderGateway = services.GetService<IOrderGateway>();

        if (executionGateway is IBrokerageGateway brokerageGateway)
        {
            var positions = await brokerageGateway.GetPositionsAsync(ct).ConfigureAwait(false);
            var details = positions
                .Select(MapBrokerPositionToDetail)
                .ToArray();

            var source = string.IsNullOrWhiteSpace(brokerageGateway.BrokerDisplayName)
                ? brokerageGateway.GatewayId
                : brokerageGateway.BrokerDisplayName;

            var statusMessage = details.Length == 0
                ? $"No live positions returned by {source}."
                : $"Showing {details.Length} live position(s) from {source}.";

            return new ExecutionBlotterSnapshotResponse(
                Positions: details,
                IsBrokerBacked: true,
                IsLive: orderGateway?.Mode == Meridian.Execution.Models.ExecutionMode.Live || executionGateway.IsConnected,
                Source: source,
                StatusMessage: statusMessage,
                AsOf: DateTimeOffset.UtcNow);
        }

        var portfolio = services.GetService<IPortfolioState>();
        if (portfolio is null)
            return null;

        var paperPortfolio = portfolio as PaperTradingPortfolio;
        var paperPositions = portfolio.Positions.Values
            .Select(position => MapPortfolioPositionToDetail(
                position,
                paperPortfolio?.GetPositionLots(position.Symbol)))
            .ToArray();

        var paperStatus = paperPositions.Length == 0
            ? "No paper positions are open."
            : $"Showing {paperPositions.Length} paper position(s).";

        return new ExecutionBlotterSnapshotResponse(
            Positions: paperPositions,
            IsBrokerBacked: false,
            IsLive: false,
            Source: "Paper Trading",
            StatusMessage: paperStatus,
            AsOf: DateTimeOffset.UtcNow);
    }

    private static async Task<IResult> SubmitPositionActionAsync(
        ExecutionPositionDetailResponse position,
        string positionSource,
        string actionName,
        OrderSide side,
        decimal quantity,
        string? positionEffect,
        Guid? fundAccountId,
        string successVerb,
        JsonSerializerOptions jsonOptions,
        HttpContext context)
    {
        if (quantity <= 0m)
        {
            var invalid = new TradingActionResult(
                ActionId: GenerateActionId(),
                Status: "Rejected",
                Message: $"A positive quantity is required to {successVerb.ToLowerInvariant()} {position.ProductDescription}.",
                OccurredAt: DateTimeOffset.UtcNow);
            return Results.Json(invalid, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
        }

        var oms = context.RequestServices.GetService<IOrderManager>();
        if (oms is null)
        {
            return Results.Problem("Order management system is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var activeGatewayId = context.RequestServices.GetService<IExecutionGateway>()?.GatewayId;
        var blockedGateDecision = BrokerageOrderPlacementGate.Evaluate(
            context.RequestServices.GetService<BrokerageConfiguration>(),
            activeGatewayId);
        if (!blockedGateDecision.IsAllowed)
        {
            var blockedActionId = GenerateActionId();
            var message = blockedGateDecision.RejectReason ?? "Broker order routing is disabled by validation gates.";
            var blockedAuditEntry = await RecordOperatorAuditAsync(
                context,
                blockedActionId,
                action: actionName,
                outcome: "Rejected",
                message: message,
                symbol: position.Symbol,
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["positionKey"] = position.PositionKey,
                    ["reason"] = "GateBlocked",
                    ["source"] = positionSource
                }).ConfigureAwait(false);

            var blocked = new TradingActionResult(
                ActionId: blockedActionId,
                Status: "Rejected",
                Message: message,
                OccurredAt: DateTimeOffset.UtcNow,
                AuditId: blockedAuditEntry?.AuditId);
            return Results.Json(blocked, jsonOptions, statusCode: StatusCodes.Status403Forbidden);
        }

        var logger = GetLogger(context.RequestServices);
        var actionId = GenerateActionId();
        if (!TryResolveActor(context, out var actor))
        {
            return Results.Unauthorized();
        }

        var gateDecision = BrokerageOrderPlacementGate.Evaluate(
            context.RequestServices.GetService<BrokerageConfiguration>(),
            activeGatewayId);
        if (!gateDecision.IsAllowed)
        {
            var blocked = new TradingActionResult(
                ActionId: actionId,
                Status: "Rejected",
                Message: gateDecision.RejectReason ?? "Broker order routing is disabled by validation gates.",
                OccurredAt: DateTimeOffset.UtcNow);
            return Results.Json(blocked, jsonOptions, statusCode: StatusCodes.Status403Forbidden);
        }

        if (fundAccountId is { } requestedFundAccountId
            && await RequireExecutionFundAccountAccessAsync(
                requestedFundAccountId,
                UserPermission.ExecuteTrades,
                context,
                jsonOptions).ConfigureAwait(false) is { } accountScopeFailure)
        {
            return accountScopeFailure;
        }

        var metadata = MergeMetadata(
            RemoveServerOwnedExecutionMetadata(position.Metadata),
            ("actor", actor),
            ("correlationId", actionId),
            ("positionKey", position.PositionKey),
            ("positionSource", positionSource),
            ("asset_class", position.AssetClass));

        if (!string.IsNullOrWhiteSpace(positionEffect))
        {
            metadata = MergeMetadata(metadata, ("position_effect", positionEffect));
        }

        var orderRequest = new OrderRequest
        {
            Symbol = position.Symbol,
            Side = side,
            Type = OrderType.Market,
            Quantity = quantity,
            ClientOrderId = $"{actionName.ToLowerInvariant()}-{position.Symbol}-{Guid.NewGuid():N}",
            FundAccountId = fundAccountId,
            Metadata = metadata
        };

        var result = await oms.PlaceOrderAsync(orderRequest, context.RequestAborted).ConfigureAwait(false);

        // A parked order is not a rejection here either. Reporting one as Rejected invites
        // the operator to retry, and every retry mints a fresh ClientOrderId — so a single
        // close can become several parked close orders that all release on approval and
        // take the position past flat, or reverse it.
        var parked = !result.Success && result.RequiresApproval;

        if (result.Success)
        {
            logger.LogInformation(
                "Trading action {ActionId}: {Action} {PositionKey} qty {Quantity} — order {OrderId} submitted",
                actionId,
                actionName,
                position.PositionKey,
                quantity,
                result.OrderId);
        }
        else if (parked)
        {
            logger.LogInformation(
                "Trading action {ActionId}: {Action} {PositionKey} qty {Quantity} — order parked for governed approval",
                actionId,
                actionName,
                position.PositionKey,
                quantity);
        }
        else
        {
            logger.LogWarning(
                "Trading action {ActionId}: {Action} {PositionKey} — order rejected: {Reason}",
                actionId,
                actionName,
                position.PositionKey,
                result.ErrorMessage);
        }

        var auditEntry = await RecordOperatorAuditAsync(
            context,
            actionId,
            action: actionName,
            outcome: result.Success ? "Accepted" : parked ? "PendingApproval" : "Rejected",
            message: result.Success
                ? $"{successVerb} order for {position.ProductDescription} submitted."
                : parked
                    ? $"{successVerb} order for {position.ProductDescription} parked for governed approval."
                    : (result.ErrorMessage ?? $"{successVerb} order rejected for {position.ProductDescription}."),
            orderId: result.OrderId,
            symbol: position.Symbol,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["positionKey"] = position.PositionKey,
                ["quantity"] = quantity.ToString("G29"),
                ["side"] = side.ToString(),
                ["assetClass"] = position.AssetClass,
                ["source"] = positionSource
            }).ConfigureAwait(false);

        var actionResult = new TradingActionResult(
            ActionId: actionId,
            Status: result.Success ? "Accepted" : parked ? "PendingApproval" : "Rejected",
            Message: result.Success
                ? $"{successVerb} order for {position.ProductDescription} submitted (order {result.OrderId})."
                : parked
                    ? $"{successVerb} order for {position.ProductDescription} is parked for governed approval; "
                        + "an approver must release it. Do not resubmit."
                    : (result.ErrorMessage ?? $"{successVerb} order rejected."),
            OccurredAt: DateTimeOffset.UtcNow,
            AuditId: auditEntry?.AuditId);

        // 202 for a park, matching /orders/submit: the request was accepted but not routed.
        // 400 would read as "this failed, try again", and each retry mints a new
        // ClientOrderId, so the retries all park and can all release.
        return (result.Success, parked) switch
        {
            (true, _) => Results.Json(actionResult, jsonOptions),
            (false, true) => Results.Json(actionResult, jsonOptions, statusCode: StatusCodes.Status202Accepted),
            _ => Results.Json(actionResult, jsonOptions, statusCode: StatusCodes.Status400BadRequest)
        };
    }

    private static ExecutionPositionDetailResponse MapBrokerPositionToDetail(BrokerPosition position)
    {
        var quantity = position.Quantity;
        return new ExecutionPositionDetailResponse(
            PositionKey: position.PositionId ?? position.Symbol,
            Symbol: position.Symbol,
            UnderlyingSymbol: position.UnderlyingSymbol ?? position.Symbol,
            ProductDescription: string.IsNullOrWhiteSpace(position.Description) ? position.Symbol : position.Description,
            TradeId: ExtractTradeId(position.PositionId),
            Quantity: quantity,
            AverageCostBasis: position.AverageEntryPrice,
            MarketPrice: position.MarketPrice,
            MarketValue: position.MarketValue,
            UnrealisedPnl: position.UnrealizedPnl,
            RealisedPnl: 0m,
            AssetClass: position.AssetClass,
            Side: quantity < 0m ? "Sell" : "Buy",
            Expiration: position.Expiration,
            Strike: position.Strike,
            Right: position.Right,
            SupportsClose: quantity != 0m,
            SupportsUpsize: quantity != 0m,
            Metadata: position.Metadata);
    }

    private static ExecutionPositionDetailResponse MapPortfolioPositionToDetail(
        IPosition position,
        IReadOnlyList<PositionLotEntry>? lots = null)
    {
        return new ExecutionPositionDetailResponse(
            PositionKey: position.Symbol,
            Symbol: position.Symbol,
            UnderlyingSymbol: position.Symbol,
            ProductDescription: position.Symbol,
            TradeId: position.Symbol,
            Quantity: position.Quantity,
            AverageCostBasis: position.AverageCostBasis,
            MarketPrice: 0m,
            MarketValue: 0m,
            UnrealisedPnl: position.UnrealizedPnl,
            RealisedPnl: position.RealizedPnl,
            AssetClass: "equity",
            Side: position.Quantity < 0 ? "Sell" : "Buy",
            Lots: lots);
    }

    private static string? ExtractTradeId(string? positionId)
    {
        if (string.IsNullOrWhiteSpace(positionId))
            return null;

        var trimmed = positionId.TrimEnd('/');
        var slashIndex = trimmed.LastIndexOf('/');
        return slashIndex >= 0 && slashIndex < trimmed.Length - 1
            ? trimmed[(slashIndex + 1)..]
            : trimmed;
    }

    private static string GenerateActionId() => $"act-{Guid.NewGuid():N}";

    private static IResult? TryRejectOrderRoutingForPhaseGate(IServiceProvider services)
    {
        var configuration = services.GetService<BrokerageConfiguration>();
        if (configuration is null || !IsLiveProductionRouting(configuration))
        {
            return null;
        }

        if (!configuration.ReadOnlyPhaseEnabled)
        {
            return Results.BadRequest(new { error = "Order routing is blocked because the read-only phase is disabled." });
        }

        if (!configuration.PaperTradingPhaseEnabled)
        {
            return Results.BadRequest(new { error = "Order routing is blocked because the paper-trading phase is disabled." });
        }

        if (!configuration.ProductionRoutingPhaseEnabled)
        {
            return Results.BadRequest(new { error = "Order routing is blocked because production routing is disabled." });
        }

        if (!configuration.ReadOnlyVerificationPassed)
        {
            return Results.BadRequest(new { error = "Production routing gate failed: read-only verification must pass." });
        }

        if (!configuration.PaperLifecycleTestsPassed)
        {
            return Results.BadRequest(new { error = "Production routing gate failed: paper-trading lifecycle tests must pass." });
        }

        if (!configuration.ReplayEvidencePassed)
        {
            return Results.BadRequest(new { error = "Production routing gate failed: replay evidence must pass." });
        }

        return null;
    }

    private static bool IsLiveProductionRouting(BrokerageConfiguration configuration)
    {
        if (!configuration.LiveExecutionEnabled)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(configuration.Gateway))
        {
            return false;
        }

        return !string.Equals(configuration.Gateway, "paper", StringComparison.OrdinalIgnoreCase);
    }

    private static ILogger GetLogger(IServiceProvider sp) =>
        sp.GetRequiredService<ILoggerFactory>()
          .CreateLogger("Meridian.Ui.Shared.Endpoints.ExecutionEndpoints");


    private static async Task<IResult?> RequireExecutionFundAccountAccessAsync(
        Guid fundAccountId,
        UserPermission requiredPermission,
        HttpContext context,
        JsonSerializerOptions jsonOptions)
    {
        if (!EndpointAuthorization.TryGetPermissions(context, out _))
        {
            return Results.Unauthorized();
        }

        var scopedAuthorization = context.RequestServices.GetService<IScopedAuthorizationService>();
        if (scopedAuthorization is null)
        {
            return Results.Problem(
                "Fund account scope authorization is not active.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!EndpointAuthorization.HasPermission(context, UserPermission.AdminMaintenance)
            && !await EndpointAuthorization.HasScopedPermissionAsync(
                context,
                requiredPermission,
                AccessScopeKindDto.Account,
                fundAccountId,
                context.RequestAborted).ConfigureAwait(false))
        {
            return ExecutionFundAccountForbidden(jsonOptions);
        }

        var queryService = ResolveAccountQueryService(context);
        if (queryService is null)
        {
            return Results.Problem(
                "Fund account scope validation is not active.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var account = await queryService.GetAccountAsync(fundAccountId, context.RequestAborted).ConfigureAwait(false);
        return account is null ? ExecutionFundAccountForbidden(jsonOptions) : null;
    }

    private static IResult ExecutionFundAccountForbidden(JsonSerializerOptions jsonOptions)
    {
        var blocked = new TradingActionResult(
            ActionId: GenerateActionId(),
            Status: "Rejected",
            Message: "The requested fund account is not authorized for this execution action.",
            OccurredAt: DateTimeOffset.UtcNow);
        return Results.Json(blocked, jsonOptions, statusCode: StatusCodes.Status403Forbidden);
    }

    private static IAccountQueryService? ResolveAccountQueryService(HttpContext context) =>
        context.RequestServices.GetService<IAccountQueryService>();

    private static bool TryResolveActor(HttpContext context, out string actor)
        => EndpointAuthorization.TryResolveActor(context, out actor);

    private static bool HasExecutionControlMutationPermission(HttpContext context) =>
        HasExecutionTradingPermission(context, UserPermission.ManageOrders);

    private static bool HasExecutionTradingPermission(HttpContext context, UserPermission requiredPermission)
        => EndpointAuthorization.HasPermission(context, requiredPermission);

    private static async Task<IResult?> RequireScopedFundAccountOrderAccessAsync(
        HttpContext context,
        Guid? fundAccountId,
        UserPermission requiredPermission)
    {
        if (!fundAccountId.HasValue)
        {
            return null;
        }

        var scopedAuthorization = context.RequestServices.GetService<IScopedAuthorizationService>();
        if (scopedAuthorization is null ||
            !EndpointAuthorization.TryResolveActor(context, out var actor) ||
            !EndpointAuthorization.TryGetPermissions(context, out var permissions))
        {
            return EndpointHelpers.Forbidden();
        }

        var decision = await scopedAuthorization.AuthorizeAsync(
                actor,
                requiredPermission,
                AccessScopeKindDto.Account,
                fundAccountId.Value,
                permissions,
                context.RequestAborted)
            .ConfigureAwait(false);

        return decision.IsAllowed ? null : EndpointHelpers.Forbidden();
    }

    private static IResult? TryRejectClientControlledExecutionMetadata(
        OrderRequest request,
        JsonSerializerOptions jsonOptions)
    {
        if (!ContainsServerOwnedExecutionMetadata(request.Metadata))
        {
            return null;
        }

        var blocked = new OrderResult
        {
            Success = false,
            OrderId = request.ClientOrderId ?? "blocked",
            ErrorMessage = "Broker account routing and execution-control metadata must be resolved server-side; server-owned execution metadata is not accepted from client order requests."
        };
        return Results.Json(blocked, jsonOptions, statusCode: StatusCodes.Status403Forbidden);
    }

    private static bool ContainsServerOwnedExecutionMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return false;
        }

        return ExecutionOrderMetadataPolicy.ContainsClientRejectedServerOwnedKey(metadata);
    }

    private static IReadOnlyDictionary<string, string>? RemoveServerOwnedExecutionMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        return ExecutionOrderMetadataPolicy.RemoveClientRejectedServerOwnedKeys(metadata);
    }

    private static bool ContainsRestrictedBrokerRoutingMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return false;
        }

        return metadata.Keys.Any(static key => key.Equals("asset_class", StringComparison.OrdinalIgnoreCase)
            || key.Equals("assetClass", StringComparison.OrdinalIgnoreCase)
            || key.Equals("alpaca:asset_class", StringComparison.OrdinalIgnoreCase)
            || key.Equals("broker_account_id", StringComparison.OrdinalIgnoreCase)
            || key.Equals("brokerAccountId", StringComparison.OrdinalIgnoreCase)
            || key.Equals("account_id", StringComparison.OrdinalIgnoreCase)
            || key.Equals("accountId", StringComparison.OrdinalIgnoreCase)
            || key.Equals("alpaca:broker_account_id", StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> MergeMetadata(
        IReadOnlyDictionary<string, string>? metadata,
        params (string Key, string? Value)[] additions)
    {
        var merged = metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in additions)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            merged[key] = value;
        }

        return merged;
    }

    private static async Task<ExecutionAuditEntry?> RecordOperatorAuditAsync(
        HttpContext context,
        string correlationId,
        string action,
        string outcome,
        string message,
        string? orderId = null,
        string? runId = null,
        string? symbol = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var auditTrail = context.RequestServices.GetService<ExecutionAuditTrailService>();
        if (auditTrail is null)
        {
            return null;
        }

        var orderGateway = context.RequestServices.GetService<IOrderGateway>();
        return await auditTrail.RecordAsync(
            category: "OperatorAction",
            action: action,
            outcome: outcome,
            actor: TryResolveActor(context, out var actor) ? actor : "unknown",
            brokerName: orderGateway?.BrokerName,
            orderId: orderId,
            runId: runId,
            symbol: symbol,
            correlationId: correlationId,
            message: message,
            metadata: metadata,
            ct: context.RequestAborted).ConfigureAwait(false);
    }

    private static ExecutionAccountDetailSnapshot BuildLegacySingleAccountSnapshot(IPortfolioState portfolio)
    {
        var positions = portfolio.Positions.Values.Cast<ExecutionPosition>().ToArray();
        var longMv = positions.Where(static p => !p.IsShort).Sum(static p => (decimal)p.AbsoluteQuantity * p.AverageCostBasis);
        var shortMv = positions.Where(static p => p.IsShort).Sum(static p => (decimal)p.AbsoluteQuantity * p.AverageCostBasis);
        return new ExecutionAccountDetailSnapshot(
            AccountId: "default",
            DisplayName: "Default Paper Account",
            Kind: AccountKind.Brokerage,
            Cash: portfolio.Cash,
            MarginBalance: 0m,
            LongMarketValue: longMv,
            ShortMarketValue: shortMv,
            GrossExposure: longMv + shortMv,
            NetExposure: longMv - shortMv,
            UnrealisedPnl: portfolio.UnrealisedPnl,
            RealisedPnl: portfolio.RealisedPnl,
            Positions: positions,
            AsOf: DateTimeOffset.UtcNow);
    }
}

// --- DTOs for execution endpoints ---

/// <summary>Account-level snapshot returned by the execution cockpit.</summary>
public sealed record ExecutionAccountSnapshot(
    decimal Cash,
    decimal PortfolioValue,
    decimal UnrealisedPnl,
    decimal RealisedPnl,
    int PositionCount,
    DateTimeOffset AsOf);

/// <summary>Full portfolio snapshot including all positions.</summary>
public sealed record ExecutionPortfolioSnapshot(
    decimal Cash,
    decimal PortfolioValue,
    decimal UnrealisedPnl,
    decimal RealisedPnl,
    IReadOnlyList<ExecutionPosition> Positions,
    DateTimeOffset AsOf);

/// <summary>Gateway health summary.</summary>
public sealed record ExecutionGatewayHealth(
    string BrokerName,
    string Mode,
    bool IsAvailable,
    DateTimeOffset AsOf,
    string? SelectedGatewayId = null);

/// <summary>Request to create a new paper trading session.</summary>
public sealed record CreatePaperSessionRequest(
    string StrategyId,
    string? StrategyName,
    decimal InitialCash = 100_000m,
    IReadOnlyList<string>? Symbols = null);

/// <summary>
/// Structured result returned by every Trading write action (cancel, close, pause, etc.).
/// Carries a correlation ID so UI and backend audit logs can be cross-referenced.
/// </summary>
public sealed record TradingActionResult(
    string ActionId,
    string Status,
    string Message,
    DateTimeOffset OccurredAt,
    string? AuditId = null);

/// <summary>Request to update the global execution circuit breaker.</summary>
public sealed record UpdateExecutionCircuitBreakerRequest(
    bool IsOpen,
    string? Reason = null,
    string? CorrelationId = null);

/// <summary>Request to update the default execution position limit.</summary>
public sealed record UpdateExecutionDefaultPositionLimitRequest(
    decimal? MaxPositionSize,
    string? Reason = null);

/// <summary>Request to update or clear a symbol-specific execution position limit.</summary>
public sealed record UpdateExecutionSymbolPositionLimitRequest(
    decimal? MaxPositionSize,
    string? Reason = null);

/// <summary>Request to create an execution manual override.</summary>
public sealed record CreateExecutionManualOverrideRequest(
    string Kind,
    string Reason,
    string? Symbol = null,
    string? StrategyId = null,
    string? RunId = null,
    DateTimeOffset? ExpiresAt = null,
    string? CorrelationId = null);

/// <summary>Request to clear an existing execution manual override.</summary>
public sealed record ClearExecutionManualOverrideRequest(
    string? Reason = null,
    string? CorrelationId = null);

/// <summary>Legacy request alias preserved for older callers.</summary>
public sealed record CreateManualOverrideCommandRequest(
    string Kind,
    string Reason,
    string? Symbol = null,
    string? StrategyId = null,
    string? RunId = null,
    DateTimeOffset? ExpiresAt = null,
    string? CorrelationId = null);

/// <summary>Legacy request alias preserved for older callers.</summary>
public sealed record ClearManualOverrideCommandRequest(
    string? Reason = null,
    string? CorrelationId = null);
