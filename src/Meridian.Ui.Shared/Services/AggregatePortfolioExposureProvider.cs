using Meridian.Domain.Collectors;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
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
/// Accepted-but-unfilled orders reserve their remaining exposure in the snapshot, so a
/// burst of working orders cannot each observe a flat book and collectively breach a
/// ceiling that none of them breaches alone.
/// </summary>
public sealed class AggregatePortfolioExposureProvider : IPortfolioExposureProvider
{
    private readonly IAggregatePortfolioService _aggregatePortfolio;
    private readonly IPortfolioState? _portfolioState;
    private readonly PortfolioRegistry? _registry;
    private readonly QuoteCollector? _quotes;
    private readonly TradeDataCollector? _trades;
    private readonly Func<IOrderManager?>? _orderManagerAccessor;

    public AggregatePortfolioExposureProvider(
        IAggregatePortfolioService aggregatePortfolio,
        IPortfolioState? portfolioState = null,
        PortfolioRegistry? registry = null,
        QuoteCollector? quotes = null,
        TradeDataCollector? trades = null,
        Func<IOrderManager?>? orderManagerAccessor = null)
    {
        _aggregatePortfolio = aggregatePortfolio ?? throw new ArgumentNullException(nameof(aggregatePortfolio));
        _portfolioState = portfolioState;
        _registry = registry;
        _quotes = quotes;
        _trades = trades;
        // Resolved lazily: the OMS depends on the risk validator, which depends on this
        // provider, so a direct constructor dependency would close a DI cycle.
        _orderManagerAccessor = orderManagerAccessor;
    }

    /// <summary>
    /// Folds accepted-but-unfilled order quantity into the exposure snapshot so working
    /// orders reserve their projected exposure. Without this, two orders that each fit
    /// under a ceiling can both pass while neither has filled, leaving their combined
    /// notional executable. Only the unfilled remainder counts — the filled portion is
    /// already carried by the positions above.
    /// </summary>
    private void ApplyWorkingOrderExposure(
        Dictionary<string, SymbolExposure> symbolExposures,
        ref decimal grossExposure,
        ref decimal netExposure)
    {
        var orderManager = _orderManagerAccessor?.Invoke();
        if (orderManager is null)
        {
            return;
        }

        foreach (var order in orderManager.GetOpenOrders())
        {
            var remaining = Math.Abs(order.Quantity) - Math.Abs(order.FilledQuantity);
            if (remaining <= 0m)
            {
                continue;
            }

            var existing = symbolExposures.TryGetValue(order.Symbol, out var tracked)
                ? tracked
                : null;
            var price = order.LimitPrice ?? order.StopPrice ?? 0m;
            if (price <= 0m)
            {
                price = WorkstationEndpoints.ResolveLiveMark(order.Symbol, _quotes, _trades) ?? 0m;
            }

            if (price <= 0m)
            {
                price = existing?.ReferencePrice ?? 0m;
            }

            decimal workingNotional;
            if (order.RoutedNotional is { } routedNotional && routedNotional > 0m)
            {
                // Broker-native notional sizing: the gateway routes dollars and discards
                // quantity, so quantity x price would reserve a fraction of what is live.
                // Scale by the unfilled fraction so partial fills do not double-count.
                var totalQuantity = Math.Abs(order.Quantity);
                workingNotional = totalQuantity > 0m
                    ? routedNotional * (remaining / totalQuantity)
                    : routedNotional;
            }
            else if (price > 0m)
            {
                workingNotional = remaining * price;
            }
            else
            {
                // No price reference at all: the order cannot be measured, and guessing a
                // price would be worse than under-reserving a market order in a never-held
                // symbol (the per-order notional rule declines to guess for the same reason).
                continue;
            }
            var signedNotional = order.Side switch
            {
                OrderSide.Buy => workingNotional,
                OrderSide.Sell => -workingNotional,
                _ => 0m
            };
            var signedQuantity = order.Side switch
            {
                OrderSide.Buy => remaining,
                OrderSide.Sell => -remaining,
                _ => 0m
            };

            grossExposure += workingNotional;
            netExposure += signedNotional;

            // Attribute the reserve to the order's own accounting scope so direction-aware
            // projections stay exact; an unscoped working order lands in its own bucket,
            // which correctly forces the additive worst case for multi-account symbols.
            var accountKey = order.FundAccountId?.ToString("D") ?? "unscoped";
            var accountNet = existing?.AccountNetNotional is { } trackedAccounts
                ? new Dictionary<string, decimal>(trackedAccounts, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            accountNet[accountKey] = accountNet.GetValueOrDefault(accountKey) + signedNotional;

            symbolExposures[order.Symbol] = new SymbolExposure(
                Symbol: existing?.Symbol ?? order.Symbol,
                GrossExposure: (existing?.GrossExposure ?? 0m) + workingNotional,
                NetQuantity: (existing?.NetQuantity ?? 0m) + signedQuantity,
                ReferencePrice: existing is { ReferencePrice: > 0m } ? existing.ReferencePrice : price,
                NetNotional: (existing?.NetNotional ?? 0m) + signedNotional,
                AccountNetNotional: accountNet);
        }
    }

    /// <inheritdoc />
    public decimal? TryGetReferencePrice(string symbol)
    {
        var mark = WorkstationEndpoints.ResolveLiveMark(symbol, _quotes, _trades);
        return mark > 0m ? mark : null;
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
            // Per-account signed exposure: direction-aware projections must know which
            // contribution an order changes, since offsetting books across accounts make
            // the aggregate net useless for deciding whether an order adds or reduces risk.
            var accountNet = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var contribution in position.Contributions)
            {
                var price = liveMark is { } mark && mark > 0m ? mark : Math.Abs(contribution.CostBasis);
                symbolGross += Math.Abs(contribution.Quantity) * price;
                symbolNet += contribution.Quantity * price;
                absoluteQuantity += Math.Abs(contribution.Quantity);
                accountNet[contribution.AccountId] =
                    accountNet.GetValueOrDefault(contribution.AccountId) + (contribution.Quantity * price);
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
                NetNotional: symbolNet,
                AccountNetNotional: accountNet);
        }

        ApplyWorkingOrderExposure(symbolExposures, ref grossExposure, ref netExposure);

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
