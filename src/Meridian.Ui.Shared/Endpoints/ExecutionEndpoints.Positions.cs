using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Identity.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Endpoints;

public static partial class ExecutionEndpoints
{
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

    /// <summary>
    /// Explains why a requested lot selection cannot be applied to a close, or returns
    /// <see langword="null"/> when the request is safe to submit.
    /// </summary>
    /// <remarks>
    /// A close is submitted as an ordinary order, and an order carries no lot instruction: lots are
    /// relieved later, at the fill, by whatever method the position's book applies. A selection is
    /// therefore accepted only when it names the method that will be applied anyway. Everything else
    /// is refused, because accepting it would relieve different lots than the operator asked for.
    /// </remarks>
    private static string? DescribeUnsupportedLotSelection(
        PositionLotSelection? selection,
        ExecutionBlotterSnapshotResponse snapshot,
        IServiceProvider services)
    {
        if (selection is null)
        {
            return null;
        }

        const string Remedy = "Remove lotSelection to close the position.";

        if (selection.Method == PositionLotSelectionMethod.SpecificId || !string.IsNullOrWhiteSpace(selection.SpecificLotId))
        {
            return $"A specific lot cannot be closed from the blotter: the close order carries no lot instruction. {Remedy}";
        }

        if (snapshot.IsBrokerBacked)
        {
            return $"Lot selection {selection.Method} cannot be honoured for a position held at {snapshot.Source}: "
                + $"the close order carries no lot instruction, so the broker's own relief method applies. {Remedy}";
        }

        if (services.GetService<IPortfolioState>() is not PaperTradingPortfolio paperPortfolio)
        {
            return $"Lot selection {selection.Method} cannot be honoured: the close order carries no lot instruction. {Remedy}";
        }

        return selection.Method == paperPortfolio.LotSelectionMethod
            ? null
            : $"Lot selection {selection.Method} cannot be honoured: this paper portfolio relieves lots by "
                + $"{paperPortfolio.LotSelectionMethod}. Remove lotSelection or request {paperPortfolio.LotSelectionMethod}.";
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
        if (!TryResolveActor(context, out var actor))
        {
            return Results.Unauthorized();
        }

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
}
