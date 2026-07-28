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
}
