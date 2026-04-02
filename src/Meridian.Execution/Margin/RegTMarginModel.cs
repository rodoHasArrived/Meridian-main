using Meridian.Execution.Models;

namespace Meridian.Execution.Margin;

/// <summary>
/// Reg T (Regulation T) margin model for U.S. equities.
/// Initial margin is 50 % of the purchase price for long positions; maintenance
/// margin is 25 % of the current market value (standard FINRA requirement).
/// Short positions use 150 % of the short proceeds as initial margin.
/// </summary>
public sealed class RegTMarginModel : IMarginModel
{
    /// <summary>
    /// Initialises a Reg T model with the standard FINRA/SEC defaults or custom rates.
    /// </summary>
    /// <param name="longInitialRate">
    ///     Initial margin rate for long positions. Defaults to 0.50 (50 %).
    /// </param>
    /// <param name="longMaintenanceRate">
    ///     Maintenance margin rate for long positions. Defaults to 0.25 (25 %).
    /// </param>
    /// <param name="shortInitialRate">
    ///     Initial margin rate for short positions expressed as a fraction of notional.
    ///     Defaults to 1.50 (150 % — the broker retains the proceeds plus 50 %).
    /// </param>
    /// <param name="shortMaintenanceRate">
    ///     Maintenance margin rate for short positions. Defaults to 1.30 (130 %).
    /// </param>
    public RegTMarginModel(
        decimal longInitialRate = 0.50m,
        decimal longMaintenanceRate = 0.25m,
        decimal shortInitialRate = 1.50m,
        decimal shortMaintenanceRate = 1.30m)
    {
        if (longInitialRate is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(longInitialRate));
        if (longMaintenanceRate is < 0m or > 1m)
            throw new ArgumentOutOfRangeException(nameof(longMaintenanceRate));
        if (shortInitialRate < 1m)
            throw new ArgumentOutOfRangeException(nameof(shortInitialRate), "Short initial rate must be ≥ 1.0.");
        if (shortMaintenanceRate < 1m)
            throw new ArgumentOutOfRangeException(nameof(shortMaintenanceRate), "Short maintenance rate must be ≥ 1.0.");

        LongInitialRate = longInitialRate;
        LongMaintenanceRate = longMaintenanceRate;
        ShortInitialRate = shortInitialRate;
        ShortMaintenanceRate = shortMaintenanceRate;
    }

    /// <inheritdoc/>
    public string ModelName => "Reg T";

    /// <summary>Initial margin rate for long positions (default 50 %).</summary>
    public decimal LongInitialRate { get; }

    /// <summary>Maintenance margin rate for long positions (default 25 %).</summary>
    public decimal LongMaintenanceRate { get; }

    /// <summary>Initial margin rate for short positions (default 150 %).</summary>
    public decimal ShortInitialRate { get; }

    /// <summary>Maintenance margin rate for short positions (default 130 %).</summary>
    public decimal ShortMaintenanceRate { get; }

    /// <inheritdoc/>
    public MarginRequirement CalculateForPosition(
        ExecutionPosition position,
        decimal lastPrice,
        decimal portfolioEquity)
    {
        ArgumentNullException.ThrowIfNull(position);
        var notional = position.Quantity * lastPrice;

        decimal initialMargin;
        decimal maintenanceMargin;

        if (!position.IsShort)
        {
            initialMargin = Math.Abs(notional) * LongInitialRate;
            maintenanceMargin = Math.Abs(notional) * LongMaintenanceRate;
        }
        else
        {
            initialMargin = Math.Abs(notional) * ShortInitialRate;
            maintenanceMargin = Math.Abs(notional) * ShortMaintenanceRate;
        }

        return new MarginRequirement(
            Symbol: position.Symbol,
            NotionalValue: notional,
            InitialMargin: initialMargin,
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

        foreach (var (symbol, position) in positions)
        {
            if (!prices.TryGetValue(symbol, out var price)) continue;

            var notional = position.Quantity * price;
            totalNotional += notional;

            if (!position.IsShort)
            {
                totalInitialMargin += Math.Abs(notional) * LongInitialRate;
                totalMaintenanceMargin += Math.Abs(notional) * LongMaintenanceRate;
            }
            else
            {
                totalInitialMargin += Math.Abs(notional) * ShortInitialRate;
                totalMaintenanceMargin += Math.Abs(notional) * ShortMaintenanceRate;
            }
        }

        // Portfolio equity = long market value + short market value + cash
        var longMarketValue = positions.Values
            .Where(p => !p.IsShort)
            .Sum(p => prices.TryGetValue(p.Symbol, out var px) ? p.Quantity * px : 0m);

        var portfolioEquity = longMarketValue + cash;

        return new MarginRequirement(
            Symbol: null,
            NotionalValue: totalNotional,
            InitialMargin: totalInitialMargin,
            MaintenanceMargin: totalMaintenanceMargin,
            ExcessLiquidity: portfolioEquity - totalMaintenanceMargin);
    }
}
