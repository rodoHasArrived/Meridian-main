using Meridian.Execution.Models;

namespace Meridian.Execution.Margin;

/// <summary>
/// Portfolio Margin model that uses risk-based margining (similar to CBOE/TIMS methodology).
/// Margin is calculated as the maximum potential loss across a set of market-stress scenarios
/// rather than a fixed percentage of notional value.
/// </summary>
/// <remarks>
/// This is a simplified implementation suitable for backtesting and paper trading.
/// Live-account portfolio-margin calculations require broker-specific haircuts and
/// correlation adjustments not modelled here.
/// </remarks>
public sealed class PortfolioMarginModel : IMarginModel
{
    private readonly decimal _stressUpPercent;
    private readonly decimal _stressDownPercent;

    /// <summary>
    /// Initialises a Portfolio Margin model.
    /// </summary>
    /// <param name="stressUpPercent">
    ///     Upward price-move shock as a decimal fraction (e.g., 0.15 = 15 %).
    ///     Defaults to 0.15.
    /// </param>
    /// <param name="stressDownPercent">
    ///     Downward price-move shock as a decimal fraction (e.g., 0.15 = 15 %).
    ///     Defaults to 0.15.
    /// </param>
    public PortfolioMarginModel(
        decimal stressUpPercent = 0.15m,
        decimal stressDownPercent = 0.15m)
    {
        if (stressUpPercent <= 0m)
            throw new ArgumentOutOfRangeException(nameof(stressUpPercent));
        if (stressDownPercent <= 0m)
            throw new ArgumentOutOfRangeException(nameof(stressDownPercent));

        _stressUpPercent = stressUpPercent;
        _stressDownPercent = stressDownPercent;
    }

    /// <inheritdoc/>
    public string ModelName => "Portfolio Margin";

    /// <inheritdoc/>
    public MarginRequirement CalculateForPosition(
        ExecutionPosition position,
        decimal lastPrice,
        decimal portfolioEquity)
    {
        ArgumentNullException.ThrowIfNull(position);

        var notional = position.Quantity * lastPrice;
        var stressedUp = position.Quantity * (lastPrice * (1m + _stressUpPercent));
        var stressedDown = position.Quantity * (lastPrice * (1m - _stressDownPercent));

        // Initial margin = maximum loss under any stress scenario
        var maxLoss = Math.Max(notional - stressedUp, notional - stressedDown);
        maxLoss = Math.Max(maxLoss, 0m);

        // Maintenance margin is 80 % of initial under simplified portfolio margin rules
        var maintenanceMargin = maxLoss * 0.80m;

        return new MarginRequirement(
            Symbol: position.Symbol,
            NotionalValue: notional,
            InitialMargin: maxLoss,
            MaintenanceMargin: maintenanceMargin,
            ExcessLiquidity: portfolioEquity - maintenanceMargin);
    }

    /// <inheritdoc/>
    public MarginRequirement CalculatePortfolioRequirement(
        IReadOnlyDictionary<string, ExecutionPosition> positions,
        IReadOnlyDictionary<string, decimal> prices,
        decimal cash)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(prices);

        var totalNotional = 0m;
        var totalInitialMargin = 0m;
        var totalMaintenanceMargin = 0m;
        var longMarketValue = 0m;

        foreach (var (symbol, position) in positions)
        {
            if (!prices.TryGetValue(symbol, out var price))
                continue;

            var notional = position.Quantity * price;
            totalNotional += notional;

            var stressedUp = position.Quantity * (price * (1m + _stressUpPercent));
            var stressedDown = position.Quantity * (price * (1m - _stressDownPercent));
            var maxLoss = Math.Max(notional - stressedUp, notional - stressedDown);
            maxLoss = Math.Max(maxLoss, 0m);

            totalInitialMargin += maxLoss;
            totalMaintenanceMargin += maxLoss * 0.80m;

            if (!position.IsShort)
                longMarketValue += notional;
        }

        var portfolioEquity = longMarketValue + cash;

        return new MarginRequirement(
            Symbol: null,
            NotionalValue: totalNotional,
            InitialMargin: totalInitialMargin,
            MaintenanceMargin: totalMaintenanceMargin,
            ExcessLiquidity: portfolioEquity - totalMaintenanceMargin);
    }
}
