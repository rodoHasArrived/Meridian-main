using Meridian.Execution.Models;
using Meridian.Risk;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Feeds <see cref="IPortfolioExposureProvider"/> from the live
/// <see cref="IAggregatePortfolioService"/>, so the portfolio-aware pre-trade rules
/// (gross exposure, symbol concentration, order notional) evaluate against the same
/// aggregated cross-run positions the Portfolio workspace reports. Exposure follows the
/// aggregation service's convention: market values are weighted-average-cost based until
/// live marks flow through the aggregate surface. Portfolio value comes from the host
/// <see cref="IPortfolioState"/> when available, falling back to gross exposure so
/// concentration percentages stay defined for registry-only compositions.
/// </summary>
public sealed class AggregatePortfolioExposureProvider : IPortfolioExposureProvider
{
    private readonly IAggregatePortfolioService _aggregatePortfolio;
    private readonly IPortfolioState? _portfolioState;

    public AggregatePortfolioExposureProvider(
        IAggregatePortfolioService aggregatePortfolio,
        IPortfolioState? portfolioState = null)
    {
        _aggregatePortfolio = aggregatePortfolio ?? throw new ArgumentNullException(nameof(aggregatePortfolio));
        _portfolioState = portfolioState;
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
            // Aggregate per contribution: the netted weighted-average cost is meaningless for
            // offsetting long/short lots across runs (it can even go negative), so gross must
            // sum each contribution's absolute quantity at its own positive cost basis.
            var symbolGross = 0m;
            var symbolNet = 0m;
            var absoluteQuantity = 0m;
            foreach (var contribution in position.Contributions)
            {
                var price = Math.Abs(contribution.CostBasis);
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
                ReferencePrice: absoluteQuantity > 0m ? symbolGross / absoluteQuantity : 0m,
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
