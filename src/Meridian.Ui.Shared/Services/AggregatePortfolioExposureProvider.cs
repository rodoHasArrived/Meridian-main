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

        // Working orders are aggregated per (symbol, account) BEFORE being reserved.
        // Reserving them one at a time compares each against a running account total while
        // the symbol's gross still reflects only positions, so a $100k long with two
        // working $80k sells would reserve 0 for the first and $40k for the second and
        // report $140k — more than any possible fill subset can reach.
        var buckets = new Dictionary<(string Symbol, string Account), WorkingOrderBucket>();
        foreach (var order in orderManager.GetExposureReservingOrders())
        {
            var remaining = Math.Abs(order.Quantity) - Math.Abs(order.FilledQuantity);
            // Dollar-sized orders retire their reserve by filled notional below; their
            // placeholder quantity says nothing about how much is still working.
            var isNotionalSized = order.RoutedNotional is > 0m;
            if (remaining <= 0m && !isNotionalSized)
            {
                continue;
            }

            var existing = symbolExposures.GetValueOrDefault(order.Symbol);
            var mark = WorkstationEndpoints.ResolveLiveMark(order.Symbol, _quotes, _trades) ?? 0m;
            if (mark <= 0m)
            {
                mark = existing?.ReferencePrice ?? 0m;
            }

            // Value a working order exactly as the pre-trade gate valued it, or the reserve
            // contradicts the decision that let it through: a marketable sell limit executes
            // at the market (a 1,000-share sell limited at $1 with the symbol at $100 is a
            // $100k order, not $1k), while a buy limit caps what is paid.
            var orderPrice = order.LimitPrice ?? order.StopPrice ?? 0m;
            var price = (orderPrice > 0m, order.Side) switch
            {
                (true, OrderSide.Sell) => Math.Max(orderPrice, mark),
                (true, _) => orderPrice,
                _ => mark
            };

            decimal workingNotional;
            if (order.RoutedNotional is { } routedNotional && routedNotional > 0m)
            {
                // Broker-native notional sizing: the gateway routes dollars and discards
                // quantity, so the submitted quantity is a placeholder and the filled
                // fraction cannot be derived from it. Retire the reserve by filled DOLLARS
                // (filled shares at their average fill price) instead, or a one-share
                // partial fill against a placeholder quantity of 1 would release the whole
                // reserve while most of the broker order is still working.
                var filledNotional = order.AverageFillPrice is { } averageFill && averageFill > 0m
                    ? Math.Abs(order.FilledQuantity) * averageFill
                    : 0m;
                workingNotional = Math.Max(0m, routedNotional - filledNotional);
                if (workingNotional <= 0m)
                {
                    continue;
                }
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

            var direction = order.Side switch
            {
                OrderSide.Buy => 1m,
                OrderSide.Sell => -1m,
                _ => 0m
            };
            // An unscoped working order lands in its own bucket, which correctly forces the
            // additive worst case for multi-account symbols.
            var key = (order.Symbol, order.FundAccountId?.ToString("D") ?? "unscoped");
            var bucket = buckets.GetValueOrDefault(key) ?? new WorkingOrderBucket();
            bucket.SignedNotional += direction * workingNotional;
            bucket.SignedQuantity += direction * Math.Max(0m, remaining);
            bucket.GrossNotional += workingNotional;
            // Orders fill independently, so the reserve must cover the worst subset, not
            // just the all-filled net: a flat account with a working $100k buy AND a
            // working $100k sell nets to zero but either alone creates $100k of exposure.
            if (direction > 0m)
            {
                bucket.BuyNotional += workingNotional;
            }
            else if (direction < 0m)
            {
                bucket.SellNotional += workingNotional;
            }

            bucket.ReferencePrice = bucket.ReferencePrice > 0m ? bucket.ReferencePrice : price;
            buckets[key] = bucket;
        }

        foreach (var ((symbol, accountKey), bucket) in buckets)
        {
            var existing = symbolExposures.GetValueOrDefault(symbol);
            var accountNet = existing?.AccountNetNotional is { } trackedAccounts
                ? new Dictionary<string, decimal>(trackedAccounts, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var accountBefore = accountNet.GetValueOrDefault(accountKey);
            var accountAfter = accountBefore + bucket.SignedNotional;
            accountNet[accountKey] = accountAfter;

            // Orders that reduce their account's position add no gross exposure — they
            // retire some — but each order fills independently, so the reserve covers the
            // largest absolute exposure ANY fill subset can reach. That extreme is all
            // buys filling (nothing else) or all sells filling; every mixed subset lies
            // between them. Capped by the notional actually working, so the reserve stays
            // conservative without inventing exposure no subset could produce.
            var worstReachable = Math.Max(
                Math.Abs(accountBefore + bucket.BuyNotional),
                Math.Abs(accountBefore - bucket.SellNotional));
            var accountGrossDelta = Math.Max(0m, worstReachable - Math.Abs(accountBefore));
            var reservedGross = Math.Min(bucket.GrossNotional, accountGrossDelta);

            grossExposure += reservedGross;
            netExposure += bucket.SignedNotional;

            symbolExposures[symbol] = new SymbolExposure(
                Symbol: existing?.Symbol ?? symbol,
                GrossExposure: (existing?.GrossExposure ?? 0m) + reservedGross,
                NetQuantity: (existing?.NetQuantity ?? 0m) + bucket.SignedQuantity,
                ReferencePrice: existing is { ReferencePrice: > 0m } ? existing.ReferencePrice : bucket.ReferencePrice,
                NetNotional: (existing?.NetNotional ?? 0m) + bucket.SignedNotional,
                AccountNetNotional: accountNet);
        }
    }

    /// <summary>Working-order exposure accumulated for one symbol and account.</summary>
    private sealed class WorkingOrderBucket
    {
        public decimal SignedNotional { get; set; }
        public decimal SignedQuantity { get; set; }
        public decimal GrossNotional { get; set; }
        public decimal BuyNotional { get; set; }
        public decimal SellNotional { get; set; }
        public decimal ReferencePrice { get; set; }
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
