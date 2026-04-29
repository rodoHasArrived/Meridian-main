using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
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
            var oms = context.RequestServices.GetService<IOrderManager>();
            if (oms is null)
                return Results.Problem("Order management system is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var actor = ResolveActor(context);
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

            return result.Success
                ? Results.Json(result, jsonOptions, statusCode: StatusCodes.Status201Created)
                : Results.Json(result, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
        })
        .WithName("SubmitOrder")
        .Produces<OrderResult>(201)
        .Produces<OrderResult>(400)
        .Produces(503);

        group.MapPost("/orders/{orderId}/cancel", async (string orderId, HttpContext context) =>
        {
            var oms = context.RequestServices.GetService<IOrderManager>();
            if (oms is null)
                return Results.Problem("Order management system is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

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
            var oms = context.RequestServices.GetService<IOrderManager>();
            if (oms is null)
                return Results.Problem("Order management system is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var logger = GetLogger(context.RequestServices);
            var actionId = GenerateActionId();
            var openCount = oms.GetOpenOrders().Count;

            await oms.CancelAllAsync(context.RequestAborted).ConfigureAwait(false);

            logger.LogInformation("Trading action {ActionId}: cancel-all — cancelled {Count} open orders", actionId, openCount);

            var actionResult = new TradingActionResult(
                ActionId: actionId,
                Status: "Completed",
                Message: $"Cancellation requested for {openCount} open order(s).",
                OccurredAt: DateTimeOffset.UtcNow);

            return Results.Json(actionResult, jsonOptions);
        })
        .WithName("CancelAllOrders")
        .Produces<TradingActionResult>(200)
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
            var controls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            if (controls is null)
            {
                return Results.Problem("Execution operator controls are not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var actor = ResolveActor(context);
            var snapshot = await controls
                .SetCircuitBreakerAsync(request.IsOpen, request.Reason, actor, request.CorrelationId, context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(snapshot, jsonOptions);
        })
        .WithName("UpdateExecutionCircuitBreaker")
        .Produces<ExecutionControlSnapshot>(200)
        .Produces(503);

        group.MapPost("/controls/manual-overrides", async (CreateExecutionManualOverrideRequest request, HttpContext context) =>
        {
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
                        CreatedBy: ResolveActor(context),
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
        .Produces(400)
        .Produces(503);

        group.MapPost("/controls/manual-overrides/{overrideId}/clear", async (string overrideId, ClearExecutionManualOverrideRequest request, HttpContext context) =>
        {
            var controls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            if (controls is null)
            {
                return Results.Problem("Execution operator controls are not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var cleared = await controls.ClearManualOverrideAsync(
                overrideId,
                ResolveActor(context),
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
        .Produces(404)
        .Produces(503);

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

        group.MapPost("/sessions/create", async (CreatePaperSessionRequest request, HttpContext context) =>
        {
            var persistence = context.RequestServices.GetService<PaperSessionPersistenceService>();
            if (persistence is null)
                return Results.Problem("Paper session management is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var actionId = GenerateActionId();
            var dto = new Meridian.Execution.Services.CreatePaperSessionDto(
                request.StrategyId,
                request.StrategyName,
                request.InitialCash,
                request.Symbols);
            var session = await persistence.CreateSessionAsync(dto, context.RequestAborted).ConfigureAwait(false);

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
        .Produces(503);

        group.MapPost("/sessions/{sessionId}/close", async (string sessionId, HttpContext context) =>
        {
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

            return await SubmitPositionActionAsync(
                position,
                snapshot.Source,
                actionName: "ClosePosition",
                side: position.Quantity < 0 ? OrderSide.Buy : OrderSide.Sell,
                quantity: request.Quantity ?? Math.Abs(position.Quantity),
                positionEffect: position.AssetClass.Equals("option", StringComparison.OrdinalIgnoreCase) ? "close" : null,
                successVerb: "Close",
                jsonOptions: jsonOptions,
                context: context).ConfigureAwait(false);
        })
        .WithName("ClosePositionByKey")
        .Produces<TradingActionResult>(200)
        .Produces<TradingActionResult>(400)
        .Produces(503);

        group.MapPost("/positions/actions/upsize", async (ExecutionPositionActionRequest request, HttpContext context) =>
        {
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

            return await SubmitPositionActionAsync(
                position,
                snapshot.Source,
                actionName: "UpsizePosition",
                side: position.Quantity < 0 ? OrderSide.Sell : OrderSide.Buy,
                quantity: request.Quantity ?? Math.Abs(position.Quantity),
                positionEffect: position.AssetClass.Equals("option", StringComparison.OrdinalIgnoreCase) ? "open" : null,
                successVerb: "Upsize",
                jsonOptions: jsonOptions,
                context: context).ConfigureAwait(false);
        })
        .WithName("UpsizePositionByKey")
        .Produces<TradingActionResult>(200)
        .Produces<TradingActionResult>(400)
        .Produces(503);

        group.MapPost("/positions/{symbol}/close", async (string symbol, HttpContext context) =>
        {
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

            var position = matches[0];
            return await SubmitPositionActionAsync(
                position,
                snapshot.Source,
                actionName: "ClosePosition",
                side: position.Quantity < 0 ? OrderSide.Buy : OrderSide.Sell,
                quantity: Math.Abs(position.Quantity),
                positionEffect: position.AssetClass.Equals("option", StringComparison.OrdinalIgnoreCase) ? "close" : null,
                successVerb: "Close",
                jsonOptions: jsonOptions,
                context: context).ConfigureAwait(false);
        })
        .WithName("ClosePosition")
        .Produces<TradingActionResult>(200)
        .Produces<TradingActionResult>(400)
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

        var paperPositions = portfolio.Positions.Values
            .Select(MapPortfolioPositionToDetail)
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

        var logger = GetLogger(context.RequestServices);
        var actionId = GenerateActionId();
        var actor = ResolveActor(context);
        var metadata = MergeMetadata(
            position.Metadata,
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
            Metadata = metadata
        };

        var result = await oms.PlaceOrderAsync(orderRequest, context.RequestAborted).ConfigureAwait(false);

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
            outcome: result.Success ? "Accepted" : "Rejected",
            message: result.Success
                ? $"{successVerb} order for {position.ProductDescription} submitted."
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
            Status: result.Success ? "Accepted" : "Rejected",
            Message: result.Success
                ? $"{successVerb} order for {position.ProductDescription} submitted (order {result.OrderId})."
                : (result.ErrorMessage ?? $"{successVerb} order rejected."),
            OccurredAt: DateTimeOffset.UtcNow,
            AuditId: auditEntry?.AuditId);

        return result.Success
            ? Results.Json(actionResult, jsonOptions)
            : Results.Json(actionResult, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
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

    private static ExecutionPositionDetailResponse MapPortfolioPositionToDetail(IPosition position)
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
            Side: position.Quantity < 0 ? "Sell" : "Buy");
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

    private static ILogger GetLogger(IServiceProvider sp) =>
        sp.GetRequiredService<ILoggerFactory>()
          .CreateLogger("Meridian.Ui.Shared.Endpoints.ExecutionEndpoints");

    private static string ResolveActor(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Meridian-Actor", out var actorValues))
        {
            var actor = actorValues.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(actor))
            {
                return actor;
            }
        }

        if (context.User.Identity?.IsAuthenticated == true &&
            !string.IsNullOrWhiteSpace(context.User.Identity.Name))
        {
            return context.User.Identity.Name!;
        }

        return "operator";
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
            actor: ResolveActor(context),
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
