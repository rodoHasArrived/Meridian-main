using System.Globalization;
using Meridian.Execution;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using Interop = Meridian.FSharp.Interop;

namespace Meridian.Risk.Rules;

/// <summary>
/// Rejects orders that would concentrate a single symbol beyond the configured percentage
/// of portfolio value. Inside the observe band (at or above
/// <see cref="ObserveBandFraction"/> of the cap) the order still routes but carries a
/// warning flag so the breach surfaces on the risk dashboard before it becomes blocking.
/// A <see langword="null"/> cap means the rule is not configured and approves.
/// </summary>
public sealed class SymbolConcentrationRule : IRiskRule
{
    /// <summary>Fraction of the cap at which an approved order starts carrying a warning flag.</summary>
    public const decimal ObserveBandFraction = 0.8m;

    private readonly IPortfolioExposureProvider _exposureProvider;
    private readonly Func<decimal?> _maxConcentrationPercent;
    private readonly ILogger<SymbolConcentrationRule> _logger;

    public SymbolConcentrationRule(
        IPortfolioExposureProvider exposureProvider,
        Func<decimal?> maxConcentrationPercent,
        ILogger<SymbolConcentrationRule> logger)
    {
        _exposureProvider = exposureProvider ?? throw new ArgumentNullException(nameof(exposureProvider));
        _maxConcentrationPercent = maxConcentrationPercent ?? throw new ArgumentNullException(nameof(maxConcentrationPercent));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string RuleName => "SymbolConcentration";

    /// <inheritdoc />
    public RiskRuleSeverity Severity => RiskRuleSeverity.Error;

    /// <inheritdoc />
    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
        var maxPercent = _maxConcentrationPercent();
        if (maxPercent is null or <= 0m)
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
            _logger.LogWarning("Symbol concentration rule rejected an order it cannot value against the configured limits");
            return Task.FromResult(RiskValidationResult.Unmeasurable(unmeasurable));
        }
        var orderNotional = OrderNotionalResolver.ResolveIncremental(request, snapshot, PriceForOrder, PriceForLeg);
        var signedOrderNotional = OrderNotionalResolver.ResolveIncrementalSigned(request, snapshot, PriceForOrder, PriceForLeg);
        // See GrossExposureRule: the projection is only direction-aware when the order's
        // own account contribution is known.
        var signedSymbolExposure = symbolExposure.ResolveSignedExposureFor(request.FundAccountId);
        var context = Interop.RiskInterop.CreatePortfolioContext(
            request,
            portfolioExposure: snapshot.GrossExposure,
            symbolExposure: symbolExposure.GrossExposure,
            signedSymbolExposure: signedSymbolExposure,
            portfolioValue: snapshot.PortfolioValue,
            orderNotional: orderNotional,
            signedOrderNotional: signedOrderNotional,
            maxGrossExposure: default,
            maxSymbolConcentrationPercent: maxPercent,
            maxOrderNotional: default,
            escalateOrderNotional: default);
        var decision = Interop.RiskInterop.EvaluateSymbolConcentration(context);

        if (!decision.Approved)
        {
            var reason = decision.Reasons.FirstOrDefault() ?? "Symbol concentration limit exceeded.";
            _logger.LogWarning("Concentration rule rejected the order; the projected single-name share exceeds the configured cap");
            return Task.FromResult(RiskValidationResult.Rejected(reason));
        }

        // Observe band: approved, but flag concentrations at or above the warning fraction
        // of the cap so operators see pressure building before orders start bouncing.
        // Direction-aware and gross-preserving, mirroring the F# policy projection.
        if (snapshot.PortfolioValue > 0m)
        {
            var projectedExposure = (signedOrderNotional, signedSymbolExposure) switch
            {
                ({ } signedOrder, { } signedSymbol) => Math.Max(
                    0m,
                    symbolExposure.GrossExposure
                        - Math.Abs(signedSymbol)
                        + Math.Abs(signedSymbol + signedOrder)),
                _ => symbolExposure.GrossExposure + (orderNotional ?? 0m)
            };
            var projectedPercent = projectedExposure / snapshot.PortfolioValue * 100m;
            if (projectedPercent >= maxPercent.Value * ObserveBandFraction)
            {
                var warning = string.Create(
                    CultureInfo.InvariantCulture,
                    $"SymbolConcentration: {request.Symbol} at {projectedPercent:F2}% of portfolio value is approaching the {maxPercent.Value:F2}% cap.");
                return Task.FromResult(RiskValidationResult.ApprovedWithWarnings(warning));
            }
        }

        return Task.FromResult(RiskValidationResult.Approved());
    }
}
