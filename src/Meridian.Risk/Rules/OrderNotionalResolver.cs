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
    public static decimal? Resolve(OrderRequest request, PortfolioExposureSnapshot snapshot)
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
        if (TryReadBrokerNotional(request) is { } brokerNotional)
        {
            return brokerNotional;
        }

        var referencePrice = request.LimitPrice ?? request.StopPrice;
        if (referencePrice is null or <= 0m)
        {
            var symbolPrice = snapshot.GetSymbolExposure(request.Symbol).ReferencePrice;
            referencePrice = symbolPrice > 0m ? symbolPrice : null;
        }

        return referencePrice is { } price ? Math.Abs(request.Quantity) * price : null;
    }

    private static decimal? TryReadBrokerNotional(OrderRequest request)
    {
        if (request.Metadata is null)
        {
            return null;
        }

        foreach (var key in (ReadOnlySpan<string>)["notional", "alpaca:notional"])
        {
            if (!request.Metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0m)
            {
                return value;
            }

            // The gateway also accepts a boolean flag meaning "quantity is dollars".
            if (bool.TryParse(raw, out var isNotional) && isNotional)
            {
                return Math.Abs(request.Quantity);
            }
        }

        return null;
    }

    /// <summary>
    /// Signed order notional: positive for buys, negative for sells, so direction-aware
    /// projections shrink when an order reduces the current position. Null when the order
    /// side is unknown or no price reference exists.
    /// </summary>
    public static decimal? ResolveSigned(OrderRequest request, PortfolioExposureSnapshot snapshot)
    {
        var notional = Resolve(request, snapshot);
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
