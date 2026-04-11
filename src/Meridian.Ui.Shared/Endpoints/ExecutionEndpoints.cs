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

        group.MapPost("/positions/actions/close", async (ExecutionPositionActionRequest request, HttpContext context) =>
        {
            var snapshot = await BuildBlotterSnapshotAsync(
                context.RequestServices,
                context.RequestAborted).ConfigureAwait(false);

            if (snapshot is null)
                return Results.Problem("Execution services are not active.", statusCode: StatusCodes.Status503ServiceUnavailable);

<<<<<<< HEAD
            var position = snapshot.Positions.FirstOrDefault(p =>
                string.Equals(p.PositionKey, request.PositionKey, StringComparison.OrdinalIgnoreCase));
=======
            var logger = GetLogger(context.RequestServices);
            var actionId = GenerateActionId();
            var symbolUpper = symbol.ToUpperInvariant();
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

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
<<<<<<< HEAD
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
=======
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
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
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
