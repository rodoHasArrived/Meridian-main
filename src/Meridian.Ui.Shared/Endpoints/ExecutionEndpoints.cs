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

            var positions = portfolio.Positions.Values.ToArray();
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

            var result = await oms.PlaceOrderAsync(request, context.RequestAborted).ConfigureAwait(false);

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
            if (gateway is null)
                return Results.Problem("No execution gateway is configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var health = new ExecutionGatewayHealth(
                BrokerName: gateway.BrokerName,
                Mode: gateway.Mode.ToString(),
                IsAvailable: true,
                AsOf: DateTimeOffset.UtcNow);

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

        group.MapPost("/positions/{symbol}/close", async (string symbol, HttpContext context) =>
        {
            var oms = context.RequestServices.GetService<IOrderManager>();
            var portfolio = context.RequestServices.GetService<IPortfolioState>();
            if (oms is null || portfolio is null)
                return Results.Problem("Execution services are not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var logger = GetLogger(context.RequestServices);
            var actionId = GenerateActionId();
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
                ClientOrderId = $"close-{symbolUpper}-{Guid.NewGuid():N}"
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

            var actionResult = new TradingActionResult(
                ActionId: actionId,
                Status: result.Success ? "Accepted" : "Rejected",
                Message: result.Success
                    ? $"Close order for {symbolUpper} submitted (order {result.OrderId})."
                    : (result.ErrorMessage ?? "Close order rejected."),
                OccurredAt: DateTimeOffset.UtcNow);

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
    DateTimeOffset AsOf);

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
    DateTimeOffset OccurredAt);
