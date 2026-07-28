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
        var referencePrice = request.LimitPrice ?? request.StopPrice;
        if (referencePrice is null or <= 0m)
        {
            var symbolPrice = snapshot.GetSymbolExposure(request.Symbol).ReferencePrice;
            referencePrice = symbolPrice > 0m ? symbolPrice : null;
        }

        return referencePrice is { } price ? Math.Abs(request.Quantity) * price : null;
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
