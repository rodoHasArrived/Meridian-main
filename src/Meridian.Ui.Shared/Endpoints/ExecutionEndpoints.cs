using System.Text.Json;
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

            var positions = portfolio.Positions.Values.Cast<ExecutionPosition>().ToArray();
            return Results.Json(positions, jsonOptions);
        })
        .WithName("GetExecutionPositions")
        .Produces<ExecutionPosition[]>(200)
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

            var actionId = GenerateActionId();
            var actor = ResolveActor(context);
            var enrichedRequest = request with
            {
                Metadata = MergeMetadata(
                    request.Metadata,
                    ("actor", actor),
                    ("correlationId", actionId),
                    ("runId", request.StrategyId))
            };

            var result = await oms.PlaceOrderAsync(enrichedRequest, context.RequestAborted).ConfigureAwait(false);

            await RecordOperatorAuditAsync(
                context,
                actionId,
                action: "SubmitOrder",
                outcome: result.Success ? "Accepted" : "Rejected",
                message: result.Success
                    ? $"Order {result.OrderId} submitted for {request.Symbol}."
                    : (result.ErrorMessage ?? $"Order submission rejected for {request.Symbol}."),
                orderId: result.OrderId,
                runId: request.StrategyId,
                symbol: request.Symbol,
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["side"] = request.Side.ToString(),
                    ["type"] = request.Type.ToString(),
                    ["quantity"] = request.Quantity.ToString("G29")
                }).ConfigureAwait(false);

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

            var auditEntry = await RecordOperatorAuditAsync(
                context,
                actionId,
                action: "CancelOrder",
                outcome: result.Success ? "Completed" : "Rejected",
                message: result.Success
                    ? $"Order {orderId} cancelled."
                    : (result.ErrorMessage ?? $"Cancel rejected for {orderId}."),
                orderId: orderId,
                runId: result.OrderState?.StrategyId,
                symbol: result.OrderState?.Symbol,
                metadata: result.OrderState is null
                    ? null
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["status"] = result.OrderState.Status.ToString()
                    }).ConfigureAwait(false);

            var actionResult = new TradingActionResult(
                ActionId: actionId,
                Status: result.Success ? "Completed" : "Rejected",
                Message: result.Success ? $"Order {orderId} cancelled." : (result.ErrorMessage ?? "Cancel rejected."),
                OccurredAt: DateTimeOffset.UtcNow,
                AuditId: auditEntry?.AuditId);

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

            var auditEntry = await RecordOperatorAuditAsync(
                context,
                actionId,
                action: "CancelAllOrders",
                outcome: "Completed",
                message: $"Cancellation requested for {openCount} open order(s).",
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["openOrderCount"] = openCount.ToString()
                }).ConfigureAwait(false);

            var actionResult = new TradingActionResult(
                ActionId: actionId,
                Status: "Completed",
                Message: $"Cancellation requested for {openCount} open order(s).",
                OccurredAt: DateTimeOffset.UtcNow,
                AuditId: auditEntry?.AuditId);

            return Results.Json(actionResult, jsonOptions);
        })
        .WithName("CancelAllOrders")
        .Produces<TradingActionResult>(200)
        .Produces(503);

        // --- Gateway health & capabilities ---

        group.MapGet("/health", async (HttpContext context) =>
        {
            var gateway = context.RequestServices.GetService<IOrderGateway>();
            if (gateway is null)
                return Results.Problem("No execution gateway is configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var executionGateway = context.RequestServices.GetService<IExecutionGateway>();
            var operatorControls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            BrokerHealthStatus? brokerHealth = null;

            if (executionGateway is IBrokerageGateway brokerageGateway)
            {
                brokerHealth = await brokerageGateway.CheckHealthAsync(context.RequestAborted).ConfigureAwait(false);
            }

            var controlSnapshot = operatorControls?.GetSnapshot();
            var health = new ExecutionGatewayHealth(
                BrokerName: gateway.BrokerName,
                Mode: gateway.Mode.ToString(),
                IsAvailable: brokerHealth?.IsHealthy ?? true,
                AsOf: DateTimeOffset.UtcNow,
                IsConnected: executionGateway?.IsConnected,
                IsHealthy: brokerHealth?.IsHealthy,
                CircuitBreakerOpen: controlSnapshot?.CircuitBreaker.IsOpen,
                Message: brokerHealth?.Message,
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

        // --- Governance / audit ---

        group.MapGet("/audit", async (int? take, HttpContext context) =>
        {
            var auditTrail = context.RequestServices.GetService<ExecutionAuditTrailService>();
            if (auditTrail is null)
            {
                return Results.Problem("Execution audit trail is not active.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var entries = await auditTrail.GetRecentAsync(take ?? 100, context.RequestAborted).ConfigureAwait(false);
            return Results.Json(entries, jsonOptions);
        })
        .WithName("GetExecutionAuditTrail")
        .Produces<IReadOnlyList<ExecutionAuditEntry>>(200)
        .Produces(503);

        group.MapGet("/controls", (HttpContext context) =>
        {
            var controls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            if (controls is null)
            {
                return Results.Problem("Execution operator controls are not active.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Json(controls.GetSnapshot(), jsonOptions);
        })
        .WithName("GetExecutionControls")
        .Produces<ExecutionControlSnapshot>(200)
        .Produces(503);

        group.MapPost("/controls/circuit-breaker", async (CircuitBreakerCommandRequest request, HttpContext context) =>
        {
            var controls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            if (controls is null)
            {
                return Results.Problem("Execution operator controls are not active.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var snapshot = await controls.SetCircuitBreakerAsync(
                request.IsOpen,
                request.Reason,
                ResolveActor(context),
                context.RequestAborted).ConfigureAwait(false);

            return Results.Json(snapshot, jsonOptions);
        })
        .WithName("SetExecutionCircuitBreaker")
        .Produces<ExecutionControlSnapshot>(200)
        .Produces(503);

        group.MapPost("/controls/position-limits/default", async (PositionLimitCommandRequest request, HttpContext context) =>
        {
            var controls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            if (controls is null)
            {
                return Results.Problem("Execution operator controls are not active.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var snapshot = await controls.SetDefaultPositionLimitAsync(
                request.MaxPositionSize,
                ResolveActor(context),
                request.Reason,
                context.RequestAborted).ConfigureAwait(false);

            return Results.Json(snapshot, jsonOptions);
        })
        .WithName("SetDefaultExecutionPositionLimit")
        .Produces<ExecutionControlSnapshot>(200)
        .Produces(503);

        group.MapPost("/controls/position-limits/{symbol}", async (string symbol, PositionLimitCommandRequest request, HttpContext context) =>
        {
            var controls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            if (controls is null)
            {
                return Results.Problem("Execution operator controls are not active.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var snapshot = await controls.SetSymbolPositionLimitAsync(
                symbol,
                request.MaxPositionSize,
                ResolveActor(context),
                request.Reason,
                context.RequestAborted).ConfigureAwait(false);

            return Results.Json(snapshot, jsonOptions);
        })
        .WithName("SetSymbolExecutionPositionLimit")
        .Produces<ExecutionControlSnapshot>(200)
        .Produces(503);

        group.MapPost("/controls/manual-overrides", async (ManualOverrideCommandRequest request, HttpContext context) =>
        {
            var controls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            if (controls is null)
            {
                return Results.Problem("Execution operator controls are not active.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var createdOverride = await controls.CreateManualOverrideAsync(
                new ManualOverrideRequest(
                    request.Kind,
                    request.Reason,
                    ResolveActor(context),
                    request.Symbol,
                    request.StrategyId,
                    request.RunId,
                    request.ExpiresAt),
                context.RequestAborted).ConfigureAwait(false);

            return Results.Json(createdOverride, jsonOptions, statusCode: StatusCodes.Status201Created);
        })
        .WithName("CreateExecutionManualOverride")
        .Produces<ExecutionManualOverride>(201)
        .Produces(503);

        group.MapPost("/controls/manual-overrides/{overrideId}/clear", async (string overrideId, ClearManualOverrideCommandRequest request, HttpContext context) =>
        {
            var controls = context.RequestServices.GetService<ExecutionOperatorControlService>();
            if (controls is null)
            {
                return Results.Problem("Execution operator controls are not active.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var cleared = await controls.ClearManualOverrideAsync(
                overrideId,
                ResolveActor(context),
                request.Reason,
                context.RequestAborted).ConfigureAwait(false);

            return cleared
                ? Results.Ok()
                : Results.NotFound();
        })
        .WithName("ClearExecutionManualOverride")
        .Produces(200)
        .Produces(404)
        .Produces(503);

        // --- Session management ---

        group.MapGet("/sessions", (HttpContext context) =>
        {
            var persistence = context.RequestServices.GetService<PaperSessionPersistenceService>();
            if (persistence is null)
                return Results.Json(Array.Empty<PaperSessionSummaryDto>(), jsonOptions);

            var sessions = persistence.GetSessions();
            return Results.Json(sessions, jsonOptions);
        })
        .WithName("GetExecutionSessions")
        .Produces<IReadOnlyList<PaperSessionSummaryDto>>(200);

        group.MapGet("/sessions/{sessionId}", (string sessionId, HttpContext context) =>
        {
            var persistence = context.RequestServices.GetService<PaperSessionPersistenceService>();
            if (persistence is null)
                return Results.NotFound();

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

            var dto = new Meridian.Execution.Services.CreatePaperSessionDto(request.StrategyId, request.StrategyName, request.InitialCash);
            var session = await persistence.CreateSessionAsync(dto, context.RequestAborted).ConfigureAwait(false);
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

            var closed = await persistence.CloseSessionAsync(sessionId, context.RequestAborted).ConfigureAwait(false);
            return closed ? Results.Ok() : Results.NotFound();
        })
        .WithName("CloseExecutionSession")
        .Produces(200)
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
                if (account is null) return Results.NotFound();
                return Results.Json(account.Positions.Values.Cast<ExecutionPosition>().ToArray(), jsonOptions);
            }

            if (string.Equals(accountId, "default", StringComparison.OrdinalIgnoreCase))
                return Results.Json(portfolio.Positions.Values.Cast<ExecutionPosition>().ToArray(), jsonOptions);

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

        group.MapPost("/positions/{symbol}/close", async (string symbol, HttpContext context) =>
        {
            var oms = context.RequestServices.GetService<IOrderManager>();
            var portfolio = context.RequestServices.GetService<IPortfolioState>();
            if (oms is null || portfolio is null)
                return Results.Problem("Execution services are not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var logger = GetLogger(context.RequestServices);
            var actionId = GenerateActionId();
            var actor = ResolveActor(context);
            var symbolUpper = symbol.ToUpperInvariant();

            if (!portfolio.Positions.TryGetValue(symbolUpper, out var position))
            {
                logger.LogWarning("Trading action {ActionId}: close position {Symbol} — position not found", actionId, symbolUpper);
                var notFound = new TradingActionResult(
                    ActionId: actionId,
                    Status: "Rejected",
                    Message: $"No open position found for {symbolUpper}.",
                    OccurredAt: DateTimeOffset.UtcNow);
                return Results.Json(notFound, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
            }

            var closingSide = position.IsShort ? OrderSide.Buy : OrderSide.Sell;
            var closeRequest = new OrderRequest
            {
                Symbol = symbolUpper,
                Side = closingSide,
                Type = OrderType.Market,
                Quantity = (decimal)position.AbsoluteQuantity,
                ClientOrderId = $"close-{symbolUpper}-{Guid.NewGuid():N}",
                Metadata = MergeMetadata(
                    null,
                    ("actor", actor),
                    ("correlationId", actionId))
            };

            var result = await oms.PlaceOrderAsync(closeRequest, context.RequestAborted).ConfigureAwait(false);

            if (result.Success)
            {
                logger.LogInformation("Trading action {ActionId}: close position {Symbol} qty {Quantity} — order {OrderId} submitted", actionId, symbolUpper, closeRequest.Quantity, result.OrderId);
            }
            else
            {
                logger.LogWarning("Trading action {ActionId}: close position {Symbol} — order rejected: {Reason}", actionId, symbolUpper, result.ErrorMessage);
            }

            var auditEntry = await RecordOperatorAuditAsync(
                context,
                actionId,
                action: "ClosePosition",
                outcome: result.Success ? "Accepted" : "Rejected",
                message: result.Success
                    ? $"Close order for {symbolUpper} submitted."
                    : (result.ErrorMessage ?? $"Close order rejected for {symbolUpper}."),
                orderId: result.OrderId,
                symbol: symbolUpper,
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["quantity"] = closeRequest.Quantity.ToString("G29"),
                    ["side"] = closingSide.ToString()
                }).ConfigureAwait(false);

            var actionResult = new TradingActionResult(
                ActionId: actionId,
                Status: result.Success ? "Accepted" : "Rejected",
                Message: result.Success
                    ? $"Close order for {symbolUpper} submitted (order {result.OrderId})."
                    : (result.ErrorMessage ?? "Close order rejected."),
                OccurredAt: DateTimeOffset.UtcNow,
                AuditId: auditEntry?.AuditId);

            return result.Success
                ? Results.Json(actionResult, jsonOptions)
                : Results.Json(actionResult, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
        })
        .WithName("ClosePosition")
        .Produces<TradingActionResult>(200)
        .Produces<TradingActionResult>(400)
        .Produces(503);
    }

    // ------------------------------------------------------------------ //
    // Private helpers                                                     //
    // ------------------------------------------------------------------ //

    private static string GenerateActionId() => $"act-{Guid.NewGuid():N}";

    private static ILogger GetLogger(IServiceProvider sp) =>
        sp.GetRequiredService<ILoggerFactory>()
          .CreateLogger("Meridian.Ui.Shared.Endpoints.ExecutionEndpoints");

    private static string? ResolveActor(HttpContext context)
    {
        var actor = context.Request.Headers["X-Meridian-Actor"].ToString();
        if (!string.IsNullOrWhiteSpace(actor))
        {
            return actor.Trim();
        }

        return context.User.Identity?.IsAuthenticated == true
            ? context.User.Identity.Name
            : null;
    }

    private static IReadOnlyDictionary<string, string>? MergeMetadata(
        IReadOnlyDictionary<string, string>? existing,
        params (string Key, string? Value)[] additions)
    {
        var metadata = existing is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in additions)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                metadata[key] = value;
            }
        }

        return metadata.Count == 0 ? null : metadata;
    }

    private static async Task<ExecutionAuditEntry?> RecordOperatorAuditAsync(
        HttpContext context,
        string actionId,
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

        var gateway = context.RequestServices.GetService<IExecutionGateway>();
        return await auditTrail.RecordAsync(
            category: "OperatorAction",
            action: action,
            outcome: outcome,
            actor: ResolveActor(context),
            brokerName: gateway?.GatewayId,
            orderId: orderId,
            runId: runId,
            symbol: symbol,
            correlationId: actionId,
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
    bool? IsConnected = null,
    bool? IsHealthy = null,
    bool? CircuitBreakerOpen = null,
    string? Message = null,
    string? SelectedGatewayId = null);

/// <summary>Request to open or close the execution circuit breaker.</summary>
public sealed record CircuitBreakerCommandRequest(
    bool IsOpen,
    string? Reason = null);

/// <summary>Request to update a default or symbol-specific position limit.</summary>
public sealed record PositionLimitCommandRequest(
    decimal? MaxPositionSize,
    string? Reason = null);

/// <summary>Request to create a manual execution override.</summary>
public sealed record ManualOverrideCommandRequest(
    string Kind,
    string Reason,
    string? Symbol = null,
    string? StrategyId = null,
    string? RunId = null,
    DateTimeOffset? ExpiresAt = null);

/// <summary>Request to clear an existing manual override.</summary>
public sealed record ClearManualOverrideCommandRequest(
    string? Reason = null);

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
