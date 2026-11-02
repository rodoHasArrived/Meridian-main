using Meridian.Execution.Models;
using Microsoft.Extensions.Options;

namespace Meridian.Execution.Margin;

/// <summary>
/// Reg T (Regulation T) margin model for U.S. equities.
/// Initial margin is 50 % of the purchase price for long positions; maintenance
/// margin is 25 % of the current market value (standard FINRA requirement).
/// Short positions use 150 % of the short proceeds as initial margin.
/// </summary>
/// <remarks>
/// <para>
/// Short rates follow the Reg T / FINRA 4210 convention: they are expressed against the
/// <em>credit balance</em> (short proceeds plus deposited margin). A 150 % initial rate means
/// the account must hold proceeds plus 50 %; a 130 % maintenance rate means the credit balance
/// must cover 130 % of the current short market value.
/// </para>
/// <para>
/// Every <see cref="MarginRequirement"/> this model returns is compared against <em>net</em>
/// equity (long market value − short market value + cash), which has already subtracted the
/// short liability. The short requirement is therefore reported on an equity basis:
/// <c>(rate − 1) × |short market value|</c>, i.e. 50 % initial and 30 % maintenance with the
/// defaults. Comparing the full 130 % against net equity would count the short liability twice
/// and raise margin calls on compliant accounts.
/// </para>
/// </remarks>
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
    ///     Maintenance margin rate for short positions, as a fraction of short market value that the
    ///     credit balance must cover. Defaults to 1.30 (130 %), i.e. 30 % of net equity.
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

    /// <summary>
    /// Initialises a Reg T model from configuration-bound options.
    /// </summary>
    public RegTMarginModel(IOptions<RegTMarginOptions> options)
        : this(GetOptions(options))
    {
    }

    private RegTMarginModel(RegTMarginOptions options)
        : this(
            options.LongInitialRate,
            options.LongMaintenanceRate,
            options.ShortInitialRate,
            options.ShortMaintenanceRate)
    {
    }

    private static RegTMarginOptions GetOptions(IOptions<RegTMarginOptions> options) =>
        (options ?? throw new ArgumentNullException(nameof(options))).Value;

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
        var notional = position.ExactQuantity * lastPrice;
        var (initialMargin, maintenanceMargin) = EquityBasisRequirement(notional);

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

        MarginPricing.ThrowIfAnyUnpriced(positions, prices);

        foreach (var (symbol, position) in positions)
        {
            if (position.ExactQuantity == 0m)
                continue;

            var notional = position.ExactQuantity * prices[symbol];
            totalNotional += notional;

            var (initial, maintenance) = EquityBasisRequirement(notional);
            totalInitialMargin += initial;
            totalMaintenanceMargin += maintenance;
        }

        // Portfolio (account) equity = long market value + short market value + cash.
        // Short positions carry a negative market value (a liability to buy the shares
        // back), so the signed notionals already accumulated above sum to
        // long MV − |short MV|. Excluding shorts here would overstate equity and thereby
        // understate maintenance-margin/excess-liquidity risk.
        var portfolioEquity = totalNotional + cash;

        return new MarginRequirement(
            Symbol: null,
            NotionalValue: totalNotional,
            InitialMargin: totalInitialMargin,
            MaintenanceMargin: totalMaintenanceMargin,
            ExcessLiquidity: portfolioEquity - totalMaintenanceMargin);
    }

    /// <summary>
    /// Returns the initial and maintenance requirement for a signed notional, expressed against
    /// net equity. Short rates are credit-balance rates, so the short liability (100 %) that net
    /// equity already carries is removed from them.
    /// </summary>
    private (decimal Initial, decimal Maintenance) EquityBasisRequirement(decimal signedNotional)
    {
        var gross = Math.Abs(signedNotional);
        return signedNotional < 0m
            ? (gross * (ShortInitialRate - 1m), gross * (ShortMaintenanceRate - 1m))
            : (gross * LongInitialRate, gross * LongMaintenanceRate);
    }
}
