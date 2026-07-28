using Meridian.Domain.Collectors;
using Meridian.Execution.Models;
using Meridian.Risk;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Feeds <see cref="IPortfolioExposureProvider"/> from the live
/// <see cref="IAggregatePortfolioService"/>, so the portfolio-aware pre-trade rules
/// (gross exposure, symbol concentration, order notional) evaluate against the same
/// aggregated cross-run positions the Portfolio workspace reports. Positions are valued
/// at the same live marks the trading screen shows (quote mid, then last trade), falling
/// back to each contribution's cost basis when no mark exists, so enforcement and display
/// can never diverge on price. Portfolio value comes from the host
/// <see cref="IPortfolioState"/> when available, falling back to gross exposure so
/// concentration percentages stay defined for registry-only compositions.
/// </summary>
public sealed class AggregatePortfolioExposureProvider : IPortfolioExposureProvider
{
    private readonly IAggregatePortfolioService _aggregatePortfolio;
    private readonly IPortfolioState? _portfolioState;
    private readonly QuoteCollector? _quotes;
    private readonly TradeDataCollector? _trades;

    public AggregatePortfolioExposureProvider(
        IAggregatePortfolioService aggregatePortfolio,
        IPortfolioState? portfolioState = null,
        QuoteCollector? quotes = null,
        TradeDataCollector? trades = null)
    {
        _aggregatePortfolio = aggregatePortfolio ?? throw new ArgumentNullException(nameof(aggregatePortfolio));
        _portfolioState = portfolioState;
        _quotes = quotes;
        _trades = trades;
    }

    /// <inheritdoc />
    public PortfolioExposureSnapshot GetSnapshot()
    {
        var positions = _aggregatePortfolio.GetAggregatedPositions();

        var symbolExposures = new Dictionary<string, SymbolExposure>(StringComparer.OrdinalIgnoreCase);
        var grossExposure = 0m;
        var netExposure = 0m;

        foreach (var position in positions)
        {
            // Value at the live mark when one exists (same source as the trading screen);
            // otherwise aggregate per contribution — the netted weighted-average cost is
            // meaningless for offsetting long/short lots across runs (it can even go
            // negative), so cost-based gross must sum each contribution's absolute
            // quantity at its own positive cost basis.
            var liveMark = WorkstationEndpoints.ResolveLiveMark(position.Symbol, _quotes, _trades);
            var symbolGross = 0m;
            var symbolNet = 0m;
            var absoluteQuantity = 0m;
            foreach (var contribution in position.Contributions)
            {
                var price = liveMark is { } mark && mark > 0m ? mark : Math.Abs(contribution.CostBasis);
                symbolGross += Math.Abs(contribution.Quantity) * price;
                symbolNet += contribution.Quantity * price;
                absoluteQuantity += Math.Abs(contribution.Quantity);
            }

            grossExposure += symbolGross;
            netExposure += symbolNet;

            symbolExposures[position.Symbol] = new SymbolExposure(
                Symbol: position.Symbol,
                GrossExposure: symbolGross,
                NetQuantity: position.TotalQuantity,
                ReferencePrice: liveMark is { } markPrice && markPrice > 0m
                    ? markPrice
                    : absoluteQuantity > 0m ? symbolGross / absoluteQuantity : 0m,
                NetNotional: symbolNet);
        }

        var portfolioValue = _portfolioState?.PortfolioValue ?? 0m;
        if (portfolioValue <= 0m)
        {
            portfolioValue = grossExposure;
        }

        return new PortfolioExposureSnapshot(
            GrossExposure: grossExposure,
            NetExposure: netExposure,
            PortfolioValue: portfolioValue,
            SymbolExposures: symbolExposures,
            AsOf: DateTimeOffset.UtcNow);
    }
}
