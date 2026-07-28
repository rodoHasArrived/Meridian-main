using Meridian.Execution.Sdk;

namespace Meridian.Risk.Rules;

/// <summary>
/// Resolves the notional of an order for portfolio-aware rules: explicit limit price when
/// present, otherwise the symbol's reference price from the exposure snapshot. Returns
/// <see langword="null"/> when no price reference exists (e.g. a market order in a symbol
/// the portfolio has never held), in which case notional-based rules approve rather than
/// guessing a price.
/// </summary>
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
        Func<string, decimal?>? referencePriceLookup = null)
    {
        if (request.Metadata is not null &&
            request.Metadata.TryGetValue(Meridian.Execution.Services.RiskEscalationQueueService.IncrementalNotionalMetadataKey, out var incrementalRaw) &&
            decimal.TryParse(incrementalRaw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var incremental) &&
            incremental >= 0m)
        {
            return incremental;
        }

        return Resolve(request, snapshot, referencePriceLookup);
    }

    /// <summary>
    /// Signed form of <see cref="ResolveIncremental"/>.
    /// </summary>
    public static decimal? ResolveIncrementalSigned(
        OrderRequest request,
        PortfolioExposureSnapshot snapshot,
        Func<string, decimal?>? referencePriceLookup = null)
    {
        var notional = ResolveIncremental(request, snapshot, referencePriceLookup);
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

    public static decimal? Resolve(
        OrderRequest request,
        PortfolioExposureSnapshot snapshot,
        Func<string, decimal?>? referencePriceLookup = null)
    {
        // Multi-leg and option orders are out of this resolver's measurement scope: the
        // top-level price is only the net debit/credit of the combination, so treating it
        // as a per-share price under-measures gross leg exposure and attributes it to the
        // placeholder top-level symbol. Rather than produce a confidently wrong number,
        // these orders resolve to null (rules approve without measuring); per-leg options
        // exposure with contract multipliers belongs to the deferred derivatives-risk lane.
        if (request.Legs is { Count: > 0 } || request.OptionContract is not null)
        {
            return null;
        }

        // Broker-native notional sizing (Alpaca metadata "notional"/"alpaca:notional")
        // routes the metadata dollars, not quantity x price — value exactly what routes.
        if (BrokerNotionalMetadata.TryRead(request.Metadata, request.Quantity) is { } brokerNotional)
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

        if (referencePrice is null or <= 0m)
        {
            // No caller price: measure at the market. The portfolio may be flat in this
            // symbol, but the feed can still price it and the gateway will certainly
            // execute it — that beats approving a market order unmeasured.
            referencePrice = marketPrice;
        }
        else if (marketPrice is { } mark && mark > 0m && request.Side == OrderSide.Sell)
        {
            // A sell limit is a floor, not a ceiling: a marketable sell executes at the
            // market, so a 10,000-share sale limited at $1 in a $100 symbol routes ~$1m,
            // not $10k. Value it at whichever side is larger.
            // A BUY limit is the opposite — it caps the price paid — so a resting buy
            // limit below the market is valued at its own limit. Valuing it at the mark
            // would measure $100k for a 1,000-share order limited at $1 and could reject
            // (and, at Critical severity, halt on) a harmless resting order.
            referencePrice = Math.Max(referencePrice.Value, mark);
        }

        return referencePrice is { } price and > 0m ? Math.Abs(request.Quantity) * price : null;
    }

    /// <summary>
    /// Signed order notional: positive for buys, negative for sells, so direction-aware
    /// projections shrink when an order reduces the current position. Null when the order
    /// side is unknown or no price reference exists.
    /// </summary>
    public static decimal? ResolveSigned(
        OrderRequest request,
        PortfolioExposureSnapshot snapshot,
        Func<string, decimal?>? referencePriceLookup = null)
    {
        var notional = Resolve(request, snapshot, referencePriceLookup);
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
