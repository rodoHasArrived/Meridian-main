using Meridian.Domain.Collectors;
using Meridian.Execution.Models;
using Meridian.Execution.Services;
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
/// can never diverge on price. Portfolio value spans the same scope as the positions:
/// the sum across every portfolio registered in the <see cref="PortfolioRegistry"/>
/// (the host state is itself registered, so it is counted exactly once), falling back to
/// the host <see cref="IPortfolioState"/> and finally to gross exposure so concentration
/// percentages stay defined for thinner compositions.
/// </summary>
public sealed class AggregatePortfolioExposureProvider : IPortfolioExposureProvider
{
    private readonly IAggregatePortfolioService _aggregatePortfolio;
    private readonly IPortfolioState? _portfolioState;
    private readonly PortfolioRegistry? _registry;
    private readonly QuoteCollector? _quotes;
    private readonly TradeDataCollector? _trades;

    public AggregatePortfolioExposureProvider(
        IAggregatePortfolioService aggregatePortfolio,
        IPortfolioState? portfolioState = null,
        PortfolioRegistry? registry = null,
        QuoteCollector? quotes = null,
        TradeDataCollector? trades = null)
    {
        _aggregatePortfolio = aggregatePortfolio ?? throw new ArgumentNullException(nameof(aggregatePortfolio));
        _portfolioState = portfolioState;
        _registry = registry;
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

        // The concentration denominator must cover the same portfolios the positions came
        // from: sum value across the registry (deduplicated by instance — the host state is
        // registered under its own run id, and a portfolio re-registered under a second run
        // id must not count twice), not just the host state.
        var portfolioValue = 0m;
        if (_registry is not null)
        {
            foreach (var portfolio in _registry.GetAll().Values.Distinct<IMultiAccountPortfolioState>(ReferenceEqualityComparer.Instance))
            {
                var value = portfolio.PortfolioValue;
                if (value > 0m)
                {
                    portfolioValue += value;
                }
            }
        }

        if (portfolioValue <= 0m)
        {
            portfolioValue = _portfolioState?.PortfolioValue ?? 0m;
        }

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
