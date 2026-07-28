using Meridian.Execution;
using Meridian.Execution.Sdk;
using Microsoft.Extensions.Logging;
using Interop = Meridian.FSharp.Interop;

namespace Meridian.Risk.Rules;

/// <summary>
/// Rejects orders whose notional would push portfolio-wide gross exposure past the
/// configured ceiling. Declared <see cref="RiskRuleSeverity.Critical"/>: a breach of the
/// book-level exposure cap is systemic, so <see cref="CompositeRiskValidator"/> also trips
/// the execution circuit breaker, halting further routing until an operator intervenes.
/// A <see langword="null"/> ceiling means the rule is not configured and approves.
/// </summary>
public sealed class GrossExposureRule : IRiskRule
{
    private readonly IPortfolioExposureProvider _exposureProvider;
    private readonly Func<decimal?> _maxGrossExposure;
    private readonly ILogger<GrossExposureRule> _logger;

    public GrossExposureRule(
        IPortfolioExposureProvider exposureProvider,
        Func<decimal?> maxGrossExposure,
        ILogger<GrossExposureRule> logger)
    {
        _exposureProvider = exposureProvider ?? throw new ArgumentNullException(nameof(exposureProvider));
        _maxGrossExposure = maxGrossExposure ?? throw new ArgumentNullException(nameof(maxGrossExposure));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string RuleName => "GrossExposure";

    /// <inheritdoc />
    public RiskRuleSeverity Severity => RiskRuleSeverity.Critical;

    /// <inheritdoc />
    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
    {
        var maxGrossExposure = _maxGrossExposure();
        if (maxGrossExposure is null or <= 0m)
        {
            return Task.FromResult(RiskValidationResult.Approved());
        }

        var snapshot = _exposureProvider.GetSnapshot();
        var symbolExposure = snapshot.GetSymbolExposure(request.Symbol);
        var context = Interop.RiskInterop.CreatePortfolioContext(
            request,
            portfolioExposure: snapshot.GrossExposure,
            symbolExposure: symbolExposure.GrossExposure,
            // Direction-aware projection needs the order's OWN account exposure: with a
            // long book in one account and a short book in another, the aggregate net
            // cannot say whether this order increases or decreases risk. Unresolvable
            // attribution yields null, and the policy falls back to the additive worst case.
            signedSymbolExposure: symbolExposure.ResolveSignedExposureFor(request.FundAccountId),
            portfolioValue: snapshot.PortfolioValue,
            orderNotional: OrderNotionalResolver.ResolveIncremental(request, snapshot, _exposureProvider.TryGetReferencePrice),
            signedOrderNotional: OrderNotionalResolver.ResolveIncrementalSigned(request, snapshot, _exposureProvider.TryGetReferencePrice),
            maxGrossExposure: maxGrossExposure,
            maxSymbolConcentrationPercent: default,
            maxOrderNotional: default,
            escalateOrderNotional: default);
        var decision = Interop.RiskInterop.EvaluateGrossExposure(context);

        if (!decision.Approved)
        {
            var reason = decision.Reasons.FirstOrDefault() ?? "Gross exposure limit exceeded.";
            _logger.LogWarning("Gross exposure rule rejected the order; the projected book exceeds the configured ceiling");
            return Task.FromResult(RiskValidationResult.Rejected(reason));
        }

        return Task.FromResult(RiskValidationResult.Approved());
    }
}
