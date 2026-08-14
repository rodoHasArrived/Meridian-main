using Meridian.Execution.Sdk;

namespace Meridian.Risk.Rules;

/// <summary>
/// Resolves the notional of an order for portfolio-aware rules: explicit limit price when
/// present, otherwise the symbol's reference price from the exposure snapshot. Returns
/// <see langword="null"/> when no price reference exists (e.g. a market order in a symbol
/// the portfolio has never held).
/// </summary>
/// <remarks>
/// A null result is not permission to route. Every rule with a configured ceiling calls
/// <see cref="DescribeUnmeasurable"/> and refuses the order, because an unmeasured order
/// consumes none of the limit and still executes at whatever the market gives it. Callers
/// adding a new notional-based rule must fail closed the same way rather than treating null
/// as "no breach found".
/// </remarks>
internal static class OrderNotionalResolver
{
    /// <summary>
    /// Incremental exposure an amendment adds, when the caller is a portfolio-level rule.
    /// The snapshot already reserves the working order at its current size, so gross and
    /// concentration must measure only the delta. The PER-ORDER notional rule must not use
    /// this: the broker receives the whole amended order, so a $90k order amended to $150k
    /// is a $150k order against the ceiling even though it adds only $60k to the book.
    /// </summary>
    public static decimal? ResolveIncremental(
        OrderRequest request,
        PortfolioExposureSnapshot snapshot,
        Func<string, decimal?>? referencePriceLookup = null,
        Func<string, OrderSide, decimal?>? sideAwarePriceLookup = null)
    {
        if (request.Metadata is not null &&
            request.Metadata.TryGetValue(Meridian.Execution.Services.RiskEscalationQueueService.IncrementalNotionalMetadataKey, out var incrementalRaw) &&
            decimal.TryParse(incrementalRaw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var incremental) &&
            incremental >= 0m)
        {
            return incremental;
        }

        return Resolve(request, snapshot, referencePriceLookup, sideAwarePriceLookup);
    }

    /// <summary>
    /// Signed form of <see cref="ResolveIncremental"/>.
    /// </summary>
    public static decimal? ResolveIncrementalSigned(
        OrderRequest request,
        PortfolioExposureSnapshot snapshot,
        Func<string, decimal?>? referencePriceLookup = null,
        Func<string, OrderSide, decimal?>? sideAwarePriceLookup = null)
    {
        var notional = ResolveIncremental(request, snapshot, referencePriceLookup, sideAwarePriceLookup);
        if (notional is not { } absolute)
        {
            return null;
        }

        return request.Side switch
        {
            OrderSide.Buy => absolute,
            OrderSide.Sell => -absolute,
            _ => null
        };
    }

    /// <summary>
    /// Why an order could not be valued, or <see langword="null"/> when it can be. Callers
    /// with a configured ceiling must refuse what they cannot measure — an unmeasured order
    /// consumes no limit and routes at whatever the market gives it.
    /// </summary>
    public static string? DescribeUnmeasurable(
        OrderRequest request,
        PortfolioExposureSnapshot snapshot,
        Func<string, decimal?>? referencePriceLookup = null,
        Func<string, OrderSide, decimal?>? sideAwarePriceLookup = null)
    {
        // Derivatives are valued, not refused. Blocking every option and spread whenever a
        // notional ceiling is configured would take a working order path away from the desk
        // to protect a limit those orders can instead simply consume.
        if (Resolve(request, snapshot, referencePriceLookup, sideAwarePriceLookup) is null)
        {
            return "No current price is available for this order, so its notional cannot be measured "
                + "against the configured limits.";
        }

        return null;
    }

    /// <summary>
    /// Default contract multiplier when a broker adapter did not stamp one. Equity options
    /// are 100 across every venue Meridian routes to, and assuming 1 would under-measure a
    /// contract order by two orders of magnitude.
    /// </summary>
    private const decimal DefaultContractMultiplier = 100m;

    /// <summary>
    /// Notional of an option or multi-leg order: the sum of each leg's contracts times its
    /// price times its multiplier. Legs are summed on absolute value — a spread's risk is
    /// not the netted debit, and one leg's exposure does not cancel another's for the
    /// purposes of a gross ceiling. Returns null only when no leg can be priced at all,
    /// which the caller treats the same as any other unpriceable order.
    /// </summary>
    private static decimal? ResolveDerivative(
        OrderRequest request,
        PortfolioExposureSnapshot snapshot,
        Func<string, decimal?>? referencePriceLookup,
        Func<string, OrderSide, decimal?>? sideAwarePriceLookup)
    {
        // Broker-native notional sizing still wins: it is what the gateway routes.
        if (BrokerNotionalMetadata.TryRead(request.Metadata, request.Quantity) is { } brokerNotional)
        {
            return brokerNotional;
        }

        var legs = request.Legs is { Count: > 0 } declared
            ? declared
            : [new OrderLeg
            {
                Symbol = request.Symbol,
                Side = request.Side,
                RatioQuantity = 1m,
                OptionContract = request.OptionContract
            }];

        // A multi-leg order's top-level price is the NET debit or credit of the package, not
        // what any one contract is worth: a one-contract spread limited at $1 whose legs
        // trade at $10 and $9 carries $1,900 of gross, not $200. Each leg is therefore
        // valued at its own market price, and the combination price is only a fallback for
        // an order that has no per-leg market at all (including the single-contract case,
        // where the top-level price IS that contract's premium).
        var combinationPrice = request.LimitPrice ?? request.StopPrice;
        var total = 0m;
        var priced = false;

        foreach (var leg in legs)
        {
            // Each leg crosses its OWN side of the book. A credit spread carries a buy leg
            // whose side is the opposite of the combination's, and pricing it at the
            // top-level side would value it at the midpoint of a wide book instead of the
            // ask it actually pays.
            var legPrice = sideAwarePriceLookup?.Invoke(leg.Symbol, leg.Side)
                ?? referencePriceLookup?.Invoke(leg.Symbol)
                ?? PositiveOrNull(snapshot.GetSymbolExposure(leg.Symbol).ReferencePrice)
                ?? combinationPrice;
            if (legPrice is not { } price || price <= 0m)
            {
                // Fail closed on the whole order. A partial total looks measurable while an
                // arbitrarily valuable missing leg consumes none of the limits — worse than
                // measuring nothing, because it reports a number the caller will trust.
                return null;
            }

            var multiplier = ResolveMultiplier(leg.OptionContract ?? request.OptionContract);
            total += Math.Abs(request.Quantity) * Math.Abs(leg.RatioQuantity) * price * multiplier;
            priced = true;
        }

        return priced ? total : null;
    }

    private static decimal? PositiveOrNull(decimal value) => value > 0m ? value : null;

    private static decimal ResolveMultiplier(OptionContractIdentity? contract)
    {
        if (contract is null)
        {
            // A leg with no option identity is an outright: one unit per unit.
            return 1m;
        }

        return decimal.TryParse(
            contract.Multiplier,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed) && parsed > 0m
            ? parsed
            : DefaultContractMultiplier;
    }

    public static decimal? Resolve(
        OrderRequest request,
        PortfolioExposureSnapshot snapshot,
        Func<string, decimal?>? referencePriceLookup = null,
        Func<string, OrderSide, decimal?>? sideAwarePriceLookup = null)
    {
        // Derivatives are measured with the same quantity x price arithmetic as anything
        // else, scaled by the contract multiplier — no Greeks, no VaR, just the notional
        // the contracts actually represent. A multi-leg order's top-level price is the net
        // debit/credit of the combination, so each leg is valued on its own instead.
        if (request.Legs is { Count: > 0 } || request.OptionContract is not null)
        {
            return ResolveDerivative(request, snapshot, referencePriceLookup, sideAwarePriceLookup);
        }

        var usesFaceValuePercentageOfPar =
            OrderSizingMetadata.UsesFaceValuePercentageOfPar(request.Metadata);

        // Broker-native notional sizing (Alpaca metadata "notional"/"alpaca:notional")
        // routes the metadata dollars, not quantity x price — value exactly what routes. Fixed
        // income is the exception: the gateway discards that metadata and routes Quantity as
        // face value, so the rails must ignore it too.
        if (!usesFaceValuePercentageOfPar
            && BrokerNotionalMetadata.TryRead(request.Metadata, request.Quantity) is { } brokerNotional)
        {
            return brokerNotional;
        }

        var referencePrice = request.LimitPrice ?? request.StopPrice;

        // The market reference: the live mark when the feed has one, else the symbol's
        // exposure reference price.
        var marketPrice = referencePriceLookup?.Invoke(request.Symbol);
        if (marketPrice is null or <= 0m)
        {
            var symbolPrice = snapshot.GetSymbolExposure(request.Symbol).ReferencePrice;
            marketPrice = symbolPrice > 0m ? symbolPrice : null;
        }

        // Only a limit price on a BUY caps what the order can pay. A sell limit is a floor
        // — a marketable sell executes at the market, so 10,000 shares limited at $1 in a
        // $100 symbol route ~$1m, not $10k. A buy STOP price is a trigger, not a cap: once
        // crossed the order executes at the market, so a triggered 1,000-share buy stop at
        // $1 in a $100 symbol routes ~$100k. Neither may be valued at its own price alone.
        var pricePaidIsCapped = request.Side == OrderSide.Buy && request.LimitPrice is > 0m;

        if (referencePrice is null or <= 0m)
        {
            // No caller price: measure at the market. The portfolio may be flat in this
            // symbol, but the feed can still price it and the gateway will certainly
            // execute it — that beats approving a market order unmeasured.
            referencePrice = marketPrice;
        }
        else if (marketPrice is { } mark && mark > 0m && !pricePaidIsCapped)
        {
            referencePrice = Math.Max(referencePrice.Value, mark);
        }
        // A capped buy keeps its own limit: valuing a resting buy limited at $1 in a $100
        // symbol at the mark would measure $100k for a harmless order and could reject it
        // — or, at Critical severity, halt the desk on it.

        if (referencePrice is null or <= 0m)
        {
            return null;
        }

        var price = referencePrice.Value;
        // Fixed-income Qty is par value and its clean price is a percentage of par: 100,000
        // face at 101.25 is $101,250, not $10,125,000. Scale the price *before* multiplying —
        // dividing the product instead lets the intermediate overflow on a representable result,
        // and the rules turn that exception into RISK_RULE_EVALUATION_FAILED rather than measuring
        // the order against their thresholds. A percentage of par is a fraction of par, so
        // converting it once up front is also the more direct statement of what the price means.
        var effectivePrice = usesFaceValuePercentageOfPar ? price / 100m : price;
        return Math.Abs(request.Quantity) * effectivePrice;
    }

    /// <summary>
    /// Signed order notional: positive for buys, negative for sells, so direction-aware
    /// projections shrink when an order reduces the current position. Null when the order
    /// side is unknown or no price reference exists.
    /// </summary>
    public static decimal? ResolveSigned(
        OrderRequest request,
        PortfolioExposureSnapshot snapshot,
        Func<string, decimal?>? referencePriceLookup = null,
        Func<string, OrderSide, decimal?>? sideAwarePriceLookup = null)
    {
        // A multi-leg order has no single direction. Its notional is the sum of every
        // leg's absolute value, and the gateway routes each leg's own side, so signing
        // that whole sum by the top-level side can present an all-buy combination as
        // reducing a long position and let the policy approve a real increase. Return
        // null instead: the projection falls back to the additive worst case.
        if (request.Legs is { Count: > 1 })
        {
            return null;
        }

        var notional = Resolve(request, snapshot, referencePriceLookup, sideAwarePriceLookup);
        if (notional is not { } absolute)
        {
            return null;
        }

        return request.Side switch
        {
            OrderSide.Buy => absolute,
            OrderSide.Sell => -absolute,
            _ => null
        };
    }
}
