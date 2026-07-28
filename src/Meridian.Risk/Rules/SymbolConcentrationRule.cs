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
        var symbolExposure = snapshot.GetSymbolExposure(request.Symbol).GrossExposure;
        var orderNotional = OrderNotionalResolver.Resolve(request, snapshot);
        var context = Interop.RiskInterop.CreatePortfolioContext(
            request,
            portfolioExposure: snapshot.GrossExposure,
            symbolExposure: symbolExposure,
            portfolioValue: snapshot.PortfolioValue,
            orderNotional: orderNotional,
            maxGrossExposure: default,
            maxSymbolConcentrationPercent: maxPercent,
            maxOrderNotional: default,
            escalateOrderNotional: default);
        var decision = Interop.RiskInterop.EvaluateSymbolConcentration(context);

        if (!decision.Approved)
        {
            var reason = decision.Reasons.FirstOrDefault() ?? "Symbol concentration limit exceeded.";
            _logger.LogWarning("Concentration rule rejected order for {Symbol}: {Reason}", request.Symbol, reason);
            return Task.FromResult(RiskValidationResult.Rejected(reason));
        }

        // Observe band: approved, but flag concentrations at or above the warning fraction
        // of the cap so operators see pressure building before orders start bouncing.
        if (snapshot.PortfolioValue > 0m)
        {
            var projectedPercent = (symbolExposure + (orderNotional ?? 0m)) / snapshot.PortfolioValue * 100m;
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
