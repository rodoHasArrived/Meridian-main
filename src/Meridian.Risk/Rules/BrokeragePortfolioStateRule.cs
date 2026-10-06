using Meridian.Execution;
using Meridian.Execution.Sdk;

namespace Meridian.Risk.Rules;

/// <summary>Mandatory live-account state and broker buying-power gate, independent of operator-tuned ceilings.</summary>
public sealed class BrokeragePortfolioStateRule(IPortfolioExposureProvider exposureProvider) : IRiskRule
{
    public string RuleName => "BrokeragePortfolioState";
    public int Priority => -100;
    public RiskRuleSeverity Severity => RiskRuleSeverity.Error;

    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = exposureProvider.GetSnapshot(request.FundAccountId);
        if (!snapshot.IsBrokerageSnapshot)
            return Task.FromResult(RiskValidationResult.Approved());
        if (!snapshot.IsComplete || request.FundAccountId is null || snapshot.FundAccountId != request.FundAccountId)
            return Task.FromResult(Rejected("BROKER_PORTFOLIO_BLOCKED",
                string.Join(" ", snapshot.BlockingReasons.DefaultIfEmpty("Brokerage account evidence is incomplete; synchronize and reconcile before placing orders."))));
        if (snapshot.Currency != "USD" || snapshot.Cash is null || snapshot.BuyingPower is not { } buyingPower)
            return Task.FromResult(Rejected("BROKER_BALANCE_INCOMPLETE", "Broker cash, buying power, and supported currency evidence are required."));
        if (request.Metadata is { } metadata && metadata.Any(pair =>
            pair.Key.Equals("currency", StringComparison.OrdinalIgnoreCase)
            && !pair.Value.Equals(snapshot.Currency, StringComparison.OrdinalIgnoreCase)))
            return Task.FromResult(Rejected("BROKER_CURRENCY_MISMATCH", "Order currency differs from the synchronized brokerage account."));

        decimal? Price(string symbol) => exposureProvider.TryGetExecutablePrice(symbol, request.Side);
        decimal? LegPrice(string symbol, OrderSide side) => exposureProvider.TryGetExecutablePrice(symbol, side);
        var notional = OrderNotionalResolver.Resolve(request, snapshot, Price, LegPrice);
        if (notional is not { } measured || measured <= 0m)
            return Task.FromResult(Rejected("BROKER_BUYING_POWER_UNMEASURABLE", "Order notional cannot be measured against broker buying power."));

        // A sale wholly covered by held shares releases capital. For opening or reversing
        // orders reserve the full measured notional; broker buying power already includes
        // the reconciled working book. A subsequent order change invalidates this snapshot.
        var held = snapshot.BrokerAvailableCoveredSaleQuantities.GetValueOrDefault(request.Symbol);
        var coveredSale = request.Side == OrderSide.Sell && request.Legs is not { Count: > 0 }
            && held >= request.Quantity && request.Quantity > 0m
            && BrokerNotionalMetadata.TryRead(request.Metadata, request.Quantity) is null;
        return Task.FromResult(!coveredSale && measured > Math.Max(0m, buyingPower)
            ? Rejected("BROKER_BUYING_POWER_EXCEEDED", "Order exceeds synchronized broker buying power.")
                with
            { ObservedValue = measured, LimitValue = Math.Max(0m, buyingPower) }
            : RiskValidationResult.Approved());
    }

    private static RiskValidationResult Rejected(string code, string reason) =>
        RiskValidationResult.Rejected(reason) with { Code = code };
}
