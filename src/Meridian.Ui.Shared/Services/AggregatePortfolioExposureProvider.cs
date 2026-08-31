using System.Diagnostics.CodeAnalysis;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.PaperMatching;
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
/// at the same live marks the trading screen shows (quote mid, then last trade) — but only
/// while those marks are still fresh, since a stalled feed would otherwise price risk at a
/// quote the market has left behind — falling back to each contribution's cost basis when
/// no current mark exists. Portfolio value spans the same scope as the positions:
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
    private readonly TimeSpan _markMaxAge;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<ILiveFeedAdapter?>? _liveFeedAccessor;
    private readonly Func<bool> _paperMatchingIsAuthoritative;

    /// <summary>
    /// How old a quote or trade may be and still price an order. A stalled feed keeps
    /// serving its last mark forever; valuing risk at one would let a symbol cached before
    /// an outage measure orders at a price the market left behind.
    /// </summary>
    public static readonly TimeSpan DefaultMarkMaxAge = TimeSpan.FromMinutes(5);

    public AggregatePortfolioExposureProvider(
        IAggregatePortfolioService aggregatePortfolio,
        IPortfolioState? portfolioState = null,
        PortfolioRegistry? registry = null,
        QuoteCollector? quotes = null,
        TradeDataCollector? trades = null,
        Func<IOrderManager?>? orderManagerAccessor = null,
        TimeSpan? markMaxAge = null,
        Func<DateTimeOffset>? clock = null,
        Func<ILiveFeedAdapter?>? liveFeedAccessor = null,
        Func<bool>? paperMatchingIsAuthoritative = null)
    {
        _aggregatePortfolio = aggregatePortfolio ?? throw new ArgumentNullException(nameof(aggregatePortfolio));
        _portfolioState = portfolioState;
        _registry = registry;
        _quotes = quotes;
        _trades = trades;
        // Resolved lazily: the OMS depends on the risk validator, which depends on this
        // provider, so a direct constructor dependency would close a DI cycle.
        _orderManagerAccessor = orderManagerAccessor;
        _markMaxAge = markMaxAge is { } configured && configured > TimeSpan.Zero
            ? configured
            : DefaultMarkMaxAge;
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
        _liveFeedAccessor = liveFeedAccessor;
        // Defaults to false. A host that has not said which engine decides its fills is treated as
        // a live one, because that is the posture where guessing wrong routes an unbounded order.
        _paperMatchingIsAuthoritative = paperMatchingIsAuthoritative ?? (static () => false);
    }

    /// <summary>
    /// The live mark, but only while the feed that produced it is still current. Every
    /// valuation on this provider goes through here so no risk rail can be priced off a
    /// mark the display surfaces are merely still showing: a symbol cached at $1 before an
    /// outage and now trading at $100 would measure a 1,000-share order at $1k instead of
    /// $100k and walk past every notional, exposure, and concentration ceiling. Returns
    /// <see langword="null"/> when no source is current, so the caller falls back to the
    /// order's own price or to cost basis rather than trusting a stale one.
    /// </summary>
    private decimal? ResolveMark(string symbol) => ResolveMark(symbol, side: null);

    /// <inheritdoc />
    public decimal? TryGetExecutablePrice(string symbol, OrderSide side)
    {
        var executable = ResolveMark(symbol, side);
        return executable > 0m ? executable : null;
    }

    /// <inheritdoc />
    public decimal? TryGetTouchPrice(string symbol, OrderSide side)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        // In a paper composition the matcher's observation is the authority for a limit too, and
        // for the same reason as the trigger: it carries a bar-close leg this provider has no
        // source for, so on a bar-driven session with no quote or print a perfectly ordinary limit
        // would otherwise be refused as unmeasurable while the engine evaluates it normally.
        if (_paperMatchingIsAuthoritative() && _liveFeedAccessor?.Invoke() is { } feed)
        {
            var observed = PaperMarketObservation.Capture(feed, symbol).ResolveAggressiveReferencePrice(side);
            if (observed is > 0m)
            {
                return observed;
            }
        }

        // The raw crossing price, with none of ResolveMark's max-with-mid conservatism: that
        // rule exists so a sell never under-measures the short it creates, but it puts a sell
        // reference at the midpoint, which is not a price any sell can trade at.
        if (TryGetFreshQuote(symbol, out var bbo))
        {
            var touch = side switch
            {
                OrderSide.Buy when bbo.AskPrice > 0m => bbo.AskPrice,
                OrderSide.Sell when bbo.BidPrice > 0m => bbo.BidPrice,
                _ => (decimal?)null
            };

            if (touch is { } crossed)
            {
                return crossed;
            }
        }

        // One-sided or absent book: there is no crossing price, so the last print is the closest
        // thing to one.
        var lastTrade = TryGetLastTradePrice(symbol);
        if (lastTrade is > 0m)
        {
            return lastTrade;
        }

        // Nothing traded and nothing crossable, so this order genuinely cannot be measured, and
        // saying so is the correct answer rather than a shortcoming. The valuation accessor must
        // NOT be reached for here: with a bid missing it answers with the ask — the side this
        // order would never cross — so a sell on an ask-only 100 book reads a limit of 90 as 10%
        // through the market. That is not merely wrong, it is wrong in the expensive direction:
        // the rule would record a measured FAT_FINGER breach rather than FAT_FINGER_UNMEASURABLE,
        // holding the rule Constrained and the readiness gate closed for an hour over a book that
        // never had a bid. Null routes to the unmeasurable outcome, which is exactly the
        // fail-closed-but-not-a-breach verdict that distinction exists for.
        return null;
    }

    /// <summary>
    /// Resolves the stop-trigger reference from <b>the same observation the matcher will
    /// consume</b>, rather than reconstructing it here.
    /// <para>
    /// Reconstruction was the bug. Every accessor above filters its sources through
    /// <see cref="DefaultMarkMaxAge"/>, because valuing risk off a stalled feed is how a symbol
    /// cached before an outage measures a 1,000-share order at a price the market left behind.
    /// The matcher applies no such filter: <see cref="PaperMarketObservation.Capture"/> takes the
    /// feed's cached last trade, bar, and quote as they are. So a six-minute-old print at 130 with
    /// a fresh 100 ask fires a buy stop at 125 immediately, while an age-filtered reconstruction
    /// drops that print, falls to the 100 ask, and approves the order as resting. Reconstructing
    /// the precedence <i>and</i> the freshness policy correctly is not something to get right
    /// twice — so this reads the matcher's observation directly and resolves through the matcher's
    /// own <see cref="PaperMarketObservation.ResolveStopTriggerPrice"/>.
    /// </para>
    /// <para>
    /// The question this accessor answers is not "what is this worth" but "will the engine fire
    /// this", and where the two disagree the engine is the authority. That is also why the age
    /// filter is deliberately absent here and deliberately present everywhere else on this class.
    /// Against a live broker the same resolution is the best available proxy, and it errs toward
    /// the most recent print — the conservative direction for catching a wrong-side stop.
    /// </para>
    /// <para>
    /// With no feed composed there is no matcher observation to share, so this falls back to the
    /// interface default: last trade, then bar close, then touch, from the collectors.
    /// </para>
    /// </summary>
    public decimal? TryGetTriggerReferencePrice(string symbol, OrderSide side)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        // The unfiltered, trade-preferred observation is right only where the paper matcher is the
        // engine. Against a live broker nobody here decides whether a stop fires, and the feed
        // cache retains prints indefinitely, so "prefer the print" turns into "prefer a price the
        // market may have left hours ago": a stale $50 print beside a fresh $100 ask makes a buy
        // stop at $60 look comfortably resting while the broker sees it already crossed and routes
        // it unbounded. Agreeing with an engine that is not running is not a safety property.
        if (_paperMatchingIsAuthoritative() && _liveFeedAccessor?.Invoke() is { } feed)
        {
            var observed = PaperMarketObservation.Capture(feed, symbol).ResolveStopTriggerPrice(side);
            if (observed is > 0m)
            {
                return observed;
            }
        }

        // Live posture, or no feed at all: the same precedence, but only over observations still
        // current. A stale print loses to a fresh quote here, which is the opposite of the paper
        // rule above and deliberately so. Bars arrive only through a feed, so with none composed
        // the resolution is print then touch — what the matcher would resolve from the same
        // absence.
        return TryGetLastTradePrice(symbol) ?? TryGetTouchPrice(symbol, side);
    }

    /// <inheritdoc />
    public decimal? TryGetLastTradePrice(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol) || _trades is null)
        {
            return null;
        }

        // Same freshness window as every other accessor here: a timestamp far in the future is as
        // untrustworthy as a stale one, so a bad clock cannot hold a print live indefinitely.
        var asOf = _clock();
        var recent = _trades.GetRecentTrades(symbol, 1);
        if (recent.Count > 0 &&
            recent[0].Price > 0m &&
            recent[0].Timestamp >= asOf - _markMaxAge &&
            recent[0].Timestamp <= asOf + _markMaxAge)
        {
            return recent[0].Price;
        }

        return null;
    }

    /// <summary>
    /// The symbol's best bid/offer, but only while the feed that produced it is still current.
    /// Shared by every price accessor so "current" has one definition: a timestamp far in the
    /// future is as untrustworthy as a stale one, since a bad clock or a malformed feed record
    /// must not hold a quote live indefinitely.
    /// </summary>
    private bool TryGetFreshQuote(string symbol, [NotNullWhen(true)] out BboQuotePayload? quote)
    {
        var asOf = _clock();
        if (_quotes is not null &&
            _quotes.TryGet(symbol, out var bbo) &&
            bbo is not null &&
            bbo.Timestamp >= asOf - _markMaxAge &&
            bbo.Timestamp <= asOf + _markMaxAge)
        {
            quote = bbo;
            return true;
        }

        quote = null;
        return false;
    }

    private decimal? ResolveMark(string symbol, OrderSide? side)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        var asOf = _clock();
        var earliest = asOf - _markMaxAge;
        // A timestamp far in the future is as untrustworthy as a stale one: a bad clock or
        // a malformed feed record must not hold a mark live indefinitely.
        var latest = asOf + _markMaxAge;

        if (TryGetFreshQuote(symbol, out var bbo))
        {
            // Valuing an ORDER never goes below the mark, and rises to the touch it will
            // cross. A buy pays the ask, so on a bid $1 / ask $100 book a market buy is
            // $100, not the $50.50 mid it would otherwise slip through the limits at. A
            // sell receives the bid, but the short it creates is marked at the mid and must
            // be covered at the ask — valuing it at the $1 bid would let a 1,000-share sell
            // book ~$50.5k of short exposure as a $1k increment. Taking the larger of mark
            // and touch is right on both sides: it never under-measures new exposure and
            // never over-credits a reduction.
            var touch = side switch
            {
                OrderSide.Buy when bbo.AskPrice > 0m => bbo.AskPrice,
                OrderSide.Sell when bbo.BidPrice > 0m => bbo.BidPrice,
                _ => (decimal?)null
            };
            if (bbo.MidPrice is { } midOrTouch && midOrTouch > 0m)
            {
                return touch is { } crossed ? Math.Max(midOrTouch, crossed) : midOrTouch;
            }
            if (touch is { } executable)
            {
                return executable;
            }
            if (bbo.AskPrice > 0m)
            {
                return bbo.AskPrice;
            }
            if (bbo.BidPrice > 0m)
            {
                return bbo.BidPrice;
            }
        }

        if (_trades is not null)
        {
            var recent = _trades.GetRecentTrades(symbol, 1);
            if (recent.Count > 0 &&
                recent[0].Price > 0m &&
                recent[0].Timestamp >= earliest &&
                recent[0].Timestamp <= latest)
            {
                return recent[0].Price;
            }
        }

        return null;
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
            var mark = ResolveMark(order.Symbol) ?? 0m;
            if (mark <= 0m)
            {
                mark = existing?.ReferencePrice ?? 0m;
            }

            // Value a working order exactly as the pre-trade gate valued it, or the reserve
            // contradicts the decision that let it through: a marketable sell limit executes
            // at the market (a 1,000-share sell limited at $1 with the symbol at $100 is a
            // $100k order, not $1k), and so does a triggered buy stop, whose stop price is a
            // trigger rather than a cap. Only a buy LIMIT caps what is paid.
            var orderPrice = order.LimitPrice ?? order.StopPrice ?? 0m;
            var pricePaidIsCapped = order.Side == OrderSide.Buy && order.LimitPrice is > 0m;
            var unitPrice = (orderPrice > 0m, pricePaidIsCapped) switch
            {
                (true, true) => orderPrice,
                (true, false) => Math.Max(orderPrice, mark),
                _ => mark
            };
            // A working option order reserves contract notional: 100 contracts at a $5
            // limit hold back $50k, not $500, or a second order passes the ceilings while
            // the first is still executable. A combination reserves the sum of its legs'
            // ratios for the same reason the gate charged them that way — its top-level
            // price is the net package debit, not what any leg is worth.
            var multiplier = order.ContractMultiplier > 0m ? order.ContractMultiplier : 1m;
            var legRatioTotal = order.Legs is { Count: > 0 } legs
                ? legs.Sum(leg => Math.Abs(leg.RatioQuantity))
                : 1m;
            var price = unitPrice * multiplier * (legRatioTotal > 0m ? legRatioTotal : 1m);

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
                // Scale percentage-of-par before multiplying, matching OrderNotionalResolver and the
                // amendment resolver. Dividing the product instead lets the intermediate overflow on
                // a notional that is representable, and this runs inside GetSnapshot() — so it would
                // throw during portfolio validation and risk-status reads for an order the OMS has
                // already accepted, rather than at the boundary that could still refuse it.
                var effectivePrice = order.UsesFaceValuePercentageOfPar ? price / 100m : price;
                workingNotional = remaining * effectivePrice;
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
            // Bucket under the account that actually holds this symbol's book. FundAccountId
            // is an accounting scope, while positions are keyed by execution account
            // ("default" for the paper portfolio), so bucketing a fund-scoped close under
            // its GUID would create an empty bucket and reserve the close as new exposure —
            // reporting $200k for a $100k long being closed. Same rule as the projection:
            // a single, non-fund-keyed contributing account is the whole book.
            var key = (order.Symbol, ResolveExposureAccountKey(existing, order.FundAccountId));
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

    /// <summary>
    /// Resolves the exposure-book key for a working order: its fund account when that
    /// account already holds exposure, the single contributing execution account when the
    /// mapping is unambiguous (that account is the whole book), otherwise the fund id (or
    /// "unscoped"), which correctly forces the additive worst case.
    /// </summary>
    private static string ResolveExposureAccountKey(SymbolExposure? existing, Guid? fundAccountId)
    {
        var fundKey = fundAccountId?.ToString("D");
        var accounts = existing?.AccountNetNotional;
        if (fundKey is not null && accounts is not null && accounts.ContainsKey(fundKey))
        {
            return fundKey;
        }

        if (accounts is { Count: 1 })
        {
            var onlyAccount = accounts.Keys.First();
            if (!Guid.TryParse(onlyAccount, out _))
            {
                return onlyAccount;
            }
        }

        return fundKey ?? "unscoped";
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
        var mark = ResolveMark(symbol);
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
            var liveMark = ResolveMark(position.Symbol);
            var symbolGross = 0m;
            var symbolNet = 0m;
            var absoluteQuantity = 0m;
            // Gross at the UNSCALED premium, kept only to derive a reference price. Dividing
            // multiplier-scaled gross by a contract count yields premium x multiplier, and
            // the resolver multiplies the reference price by the multiplier again — a $5
            // option would price at $50,000 per contract, breaching a ceiling it is nowhere
            // near and, at Critical severity, halting the desk on a small order.
            var symbolUnitGross = 0m;
            // Per-account signed exposure: direction-aware projections must know which
            // contribution an order changes, since offsetting books across accounts make
            // the aggregate net useless for deciding whether an order adds or reduces risk.
            var accountNet = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var contribution in position.Contributions)
            {
                // The contract multiplier is part of the price of one unit of quantity: an
                // option position of 100 contracts at a $5 premium is $50k of exposure, not
                // $500. Ignoring it would let every check after the first option fill run
                // against exposure understated by the multiplier.
                var unitPrice = liveMark is { } mark && mark > 0m ? mark : Math.Abs(contribution.CostBasis);
                var price = unitPrice * (contribution.ContractMultiplier > 0m ? contribution.ContractMultiplier : 1m);
                symbolGross += Math.Abs(contribution.Quantity) * price;
                symbolUnitGross += Math.Abs(contribution.Quantity) * unitPrice;
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
                // Per contract, never per contract-times-multiplier: every consumer
                // applies the multiplier itself.
                ReferencePrice: liveMark is { } markPrice && markPrice > 0m
                    ? markPrice
                    : absoluteQuantity > 0m ? symbolUnitGross / absoluteQuantity : 0m,
                NetNotional: symbolNet,
                AccountNetNotional: accountNet);
        }

        ApplyWorkingOrderExposure(symbolExposures, ref grossExposure, ref netExposure);

        // The concentration denominator must cover the same portfolios the positions came
        // from: sum value across the registry (deduplicated by instance — the host state is
        // registered under its own run id, and a portfolio re-registered under a second run
        // id must not count twice), not just the host state.
        var portfolioValue = 0m;
        var measuredAnyPortfolio = false;
        if (_registry is not null)
        {
            // Sum SIGNED values: positions from a loss-impaired portfolio still enter the
            // numerator, so excluding its negative NAV inflates the denominator. A +$100k
            // and a -$90k portfolio have $10k of aggregate NAV, not $100k, and treating
            // them as $100k would permit roughly ten times the configured concentration.
            foreach (var portfolio in _registry.GetAll().Values.Distinct<IMultiAccountPortfolioState>(ReferenceEqualityComparer.Instance))
            {
                portfolioValue += portfolio.PortfolioValue;
                measuredAnyPortfolio = true;
            }
        }

        // "No registered portfolio reported a value" and "the registered portfolios sum to
        // zero or less" are different facts. Only the first is missing data; the second is a
        // known insolvent book, and substituting the host NAV or gross exposure for it would
        // manufacture a positive denominator that lets percentage-of-NAV caps pass orders
        // against a portfolio that has nothing left to risk. Leave it as measured.
        if (!measuredAnyPortfolio)
        {
            portfolioValue = _portfolioState?.PortfolioValue ?? 0m;

            if (portfolioValue <= 0m)
            {
                portfolioValue = grossExposure;
            }
        }

        return new PortfolioExposureSnapshot(
            GrossExposure: grossExposure,
            NetExposure: netExposure,
            PortfolioValue: portfolioValue,
            SymbolExposures: symbolExposures,
            AsOf: DateTimeOffset.UtcNow);
    }
}
