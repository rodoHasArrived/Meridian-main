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
        var context = Interop.RiskInterop.CreatePortfolioContext(
            request,
            portfolioExposure: snapshot.GrossExposure,
            symbolExposure: snapshot.GetSymbolExposure(request.Symbol).GrossExposure,
            portfolioValue: snapshot.PortfolioValue,
            orderNotional: OrderNotionalResolver.Resolve(request, snapshot),
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
            _logger.LogWarning("Order notional rule escalated order for {Symbol}: {Reason}", request.Symbol, reason);
            return Task.FromResult(RiskValidationResult.Escalated(reason));
        }

        _logger.LogWarning("Order notional rule rejected order for {Symbol}: {Reason}", request.Symbol, reason);
        return Task.FromResult(RiskValidationResult.Rejected(reason));
    }
}
