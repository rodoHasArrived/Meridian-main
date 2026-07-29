using Meridian.Execution;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using Interop = Meridian.FSharp.Interop;

namespace Meridian.Risk.Rules;

/// <summary>
/// Gates per-order notional. Above the hard ceiling the order is rejected; at or above the
/// escalation band (but under the ceiling) the F# policy returns an escalate decision and
/// <see cref="CompositeRiskValidator"/> parks the order for governed approval instead of
/// routing it. Orders with no resolvable price reference approve — the rule never guesses
/// a price. <see langword="null"/> thresholds disable the corresponding band.
/// </summary>
public sealed class OrderNotionalRule : IRiskRule
{
    private readonly IPortfolioExposureProvider _exposureProvider;
    private readonly Func<decimal?> _maxOrderNotional;
    private readonly Func<decimal?> _escalateOrderNotional;
    private readonly ILogger<OrderNotionalRule> _logger;

    public OrderNotionalRule(
        IPortfolioExposureProvider exposureProvider,
        Func<decimal?> maxOrderNotional,
        Func<decimal?> escalateOrderNotional,
        ILogger<OrderNotionalRule> logger)
    {
        _exposureProvider = exposureProvider ?? throw new ArgumentNullException(nameof(exposureProvider));
        _maxOrderNotional = maxOrderNotional ?? throw new ArgumentNullException(nameof(maxOrderNotional));
        _escalateOrderNotional = escalateOrderNotional ?? throw new ArgumentNullException(nameof(escalateOrderNotional));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string RuleName => "OrderNotional";

    /// <inheritdoc />
    public RiskRuleSeverity Severity => RiskRuleSeverity.Error;

    /// <inheritdoc />
    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
        var maxNotional = _maxOrderNotional();
        var escalateAt = _escalateOrderNotional();
        if (maxNotional is null or <= 0m && escalateAt is null or <= 0m)
        {
            return Task.FromResult(RiskValidationResult.Approved());
        }

        var snapshot = _exposureProvider.GetSnapshot();
        var symbolExposure = snapshot.GetSymbolExposure(request.Symbol);

        // An order pays the touch, not the midpoint: with a bid of $1 and an ask of $100 a
        // market buy routes near $100, so measuring it at the $50.50 mid would let it
        // through at roughly half its real size.
        decimal? PriceForOrder(string symbol) => _exposureProvider.TryGetExecutablePrice(symbol, request.Side);
        // A combination's legs each cross their own side of the book.
        decimal? PriceForLeg(string symbol, OrderSide side) => _exposureProvider.TryGetExecutablePrice(symbol, side);

        // A configured ceiling that an unmeasurable order sails past is not a ceiling. An
        // order this resolver cannot value — a derivative, or anything with no current
        // price — consumes no limit and still routes at whatever the market gives it, so
        // refuse it instead of approving it unmeasured.
        if (OrderNotionalResolver.DescribeUnmeasurable(request, snapshot, PriceForOrder, PriceForLeg) is { } unmeasurable)
        {
            _logger.LogWarning("Order notional rule rejected an order it cannot value against the configured limits");
            return Task.FromResult(RiskValidationResult.Rejected(unmeasurable));
        }
        var context = Interop.RiskInterop.CreatePortfolioContext(
            request,
            portfolioExposure: snapshot.GrossExposure,
            symbolExposure: symbolExposure.GrossExposure,
            signedSymbolExposure: symbolExposure.ResolveSignedExposureFor(request.FundAccountId),
            portfolioValue: snapshot.PortfolioValue,
            orderNotional: OrderNotionalResolver.Resolve(request, snapshot, PriceForOrder, PriceForLeg),
            signedOrderNotional: OrderNotionalResolver.ResolveSigned(request, snapshot, PriceForOrder, PriceForLeg),
            maxGrossExposure: default,
            maxSymbolConcentrationPercent: default,
            maxOrderNotional: maxNotional,
            escalateOrderNotional: escalateAt);
        var decision = Interop.RiskInterop.EvaluateOrderNotional(context);

        if (decision.Approved)
        {
            return Task.FromResult(RiskValidationResult.Approved());
        }

        var reason = decision.Reasons.FirstOrDefault() ?? "Order notional limit breached.";
        if (string.Equals(decision.DecisionKind, "escalate", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Order notional rule escalated the order into the governed-approval band");
            return Task.FromResult(RiskValidationResult.Escalated(reason));
        }

        _logger.LogWarning("Order notional rule rejected the order; the notional exceeds the configured ceiling");
        return Task.FromResult(RiskValidationResult.Rejected(reason));
    }
}
