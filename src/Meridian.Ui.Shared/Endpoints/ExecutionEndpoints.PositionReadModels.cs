using Meridian.Contracts.Api;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.PortfolioRecords.Accounts;
using Microsoft.Extensions.DependencyInjection;

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
