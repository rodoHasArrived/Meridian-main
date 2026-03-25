using System.Text.Json;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Services;
using Meridian.Execution.Sdk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
                Positions: portfolio.Positions.Values.ToArray(),
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

            var result = await oms.CancelOrderAsync(orderId, context.RequestAborted).ConfigureAwait(false);
            return result.Success
                ? Results.Json(result, jsonOptions)
                : Results.Json(result, jsonOptions, statusCode: StatusCodes.Status400BadRequest);
        })
        .WithName("CancelOrder")
        .Produces<OrderResult>(200)
        .Produces<OrderResult>(400)
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
