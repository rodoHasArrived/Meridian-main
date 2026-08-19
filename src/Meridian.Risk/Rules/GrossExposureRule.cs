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
/// <para>
/// Reserving: the ceiling is finite capacity shared across concurrent submissions, so this rule
/// implements <see cref="IReservingRiskRule"/> and takes the order's exposure in the same atomic
/// step as the check. Evaluating without reserving let orders validated side by side each measure
/// against a snapshot none of them appeared in yet, so all passed and the book breached a limit
/// none of them breached alone.
/// </para>
/// </summary>
public sealed class GrossExposureRule : IReservingRiskRule
{
    private readonly IPortfolioExposureProvider _exposureProvider;
    private readonly Func<decimal?> _maxGrossExposure;
    private readonly ExposureReservationLedger _inFlightExposure;
    private readonly ILogger<GrossExposureRule> _logger;

    public GrossExposureRule(
        IPortfolioExposureProvider exposureProvider,
        Func<decimal?> maxGrossExposure,
        ILogger<GrossExposureRule> logger,
        ExposureReservationLedger? inFlightExposure = null)
    {
        _exposureProvider = exposureProvider ?? throw new ArgumentNullException(nameof(exposureProvider));
        _maxGrossExposure = maxGrossExposure ?? throw new ArgumentNullException(nameof(maxGrossExposure));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Defaulted so existing composition keeps working; a caller that shares one ledger across
        // the rule set gets cross-order protection, and a caller that does not gets the previous
        // per-evaluation behaviour rather than a hard break.
        _inFlightExposure = inFlightExposure ?? new ExposureReservationLedger();
    }

    /// <summary>In-flight exposure this rule reserves against, exposed for diagnostics and tests.</summary>
    internal ExposureReservationLedger InFlightExposure => _inFlightExposure;

    /// <inheritdoc />
    public string RuleName => "GrossExposure";

    /// <inheritdoc />
    public RiskRuleSeverity Severity => RiskRuleSeverity.Critical;

    /// <inheritdoc />
    public Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default)
        // Side-effect free by contract: measured against the settled book alone, with no in-flight
        // exposure counted and nothing reserved.
        => Task.FromResult(Measure(request, accountSignedInFlight: 0m, totalGrossInFlight: 0m).Result);

    /// <inheritdoc />
    public Task<RiskRuleReservationResult> EvaluateAndReserveAsync(
        OrderRequest request,
        CancellationToken ct = default)
    {
        // Observed before reserving, never after: a token seen after capacity was taken would
        // strand that capacity where nothing can release it.
        ct.ThrowIfCancellationRequested();

        var unreserved = Measure(request, accountSignedInFlight: 0m, totalGrossInFlight: 0m);

        // An order that is refused, unmeasurable, or measured against no configured ceiling takes
        // no capacity, so there is nothing to reserve and nothing for the caller to settle.
        if (!unreserved.Result.IsApproved || unreserved.GrossNotional is not { } grossNotional)
        {
            return Task.FromResult(new RiskRuleReservationResult(unreserved.Result, null));
        }

        var signedNotional = unreserved.SignedNotional ?? grossNotional;

        // Re-measured inside the ledger lock against the exposure other in-flight orders hold, and
        // the capacity taken in the same step. The approval above is only the unreserved case; an
        // order that fits the settled book may not fit once its concurrent siblings are counted.
        RiskValidationResult? reservedResult = null;
        var reservation = _inFlightExposure.TryReserve(
            request.FundAccountId,
            grossNotional,
            signedNotional,
            (accountSignedInFlight, totalGrossInFlight) =>
            {
                reservedResult = Measure(request, accountSignedInFlight, totalGrossInFlight).Result;
                return reservedResult.IsApproved;
            });

        if (reservation is not null)
        {
            return Task.FromResult(new RiskRuleReservationResult(unreserved.Result, reservation));
        }

        // Refused against in-flight exposure. Report the projected refusal, falling back to the
        // unreserved verdict if the ledger never ran the callback.
        var refusal = reservedResult ?? unreserved.Result;
        _logger.LogWarning(
            "Gross exposure rule rejected the order; the projected book including in-flight orders exceeds the configured ceiling");
        return Task.FromResult(new RiskRuleReservationResult(refusal, null));
    }

    /// <summary>
    /// Projects the order onto the settled book plus the supplied in-flight exposure and reports
    /// the verdict, along with the notional the order would consume when it is measurable.
    /// </summary>
    /// <param name="accountSignedInFlight">Signed notional the order's own account already has in flight.</param>
    /// <param name="totalGrossInFlight">Gross notional in flight across every account.</param>
    private MeasuredProjection Measure(
        OrderRequest request,
        decimal accountSignedInFlight,
        decimal totalGrossInFlight)
    {
        var maxGrossExposure = _maxGrossExposure();
        if (maxGrossExposure is null or <= 0m)
        {
            return new MeasuredProjection(RiskValidationResult.Approved(), null, null);
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
            _logger.LogWarning("Gross exposure rule rejected an order it cannot value against the configured limits");
            return new MeasuredProjection(RiskValidationResult.Unmeasurable(unmeasurable), null, null);
        }

        var orderNotional = OrderNotionalResolver.ResolveIncremental(request, snapshot, PriceForOrder, PriceForLeg);
        var signedOrderNotional = OrderNotionalResolver.ResolveIncrementalSigned(request, snapshot, PriceForOrder, PriceForLeg);

        // The order's own account carries its own in-flight orders; another account's do not
        // inform this order's direction. Left null when attribution is unresolvable, so the
        // policy keeps falling back to the additive worst case.
        var signedSymbolExposure = symbolExposure.ResolveSignedExposureFor(request.FundAccountId) is { } resolved
            ? resolved + accountSignedInFlight
            : (decimal?)null;

        var context = Interop.RiskInterop.CreatePortfolioContext(
            request,
            // Gross is a property of the whole book, so every account's in-flight notional counts.
            portfolioExposure: snapshot.GrossExposure + totalGrossInFlight,
            symbolExposure: symbolExposure.GrossExposure,
            // Direction-aware projection needs the order's OWN account exposure: with a
            // long book in one account and a short book in another, the aggregate net
            // cannot say whether this order increases or decreases risk. Unresolvable
            // attribution yields null, and the policy falls back to the additive worst case.
            signedSymbolExposure: signedSymbolExposure,
            portfolioValue: snapshot.PortfolioValue,
            orderNotional: orderNotional,
            signedOrderNotional: signedOrderNotional,
            maxGrossExposure: maxGrossExposure,
            maxSymbolConcentrationPercent: default,
            maxOrderNotional: default,
            escalateOrderNotional: default);
        var decision = Interop.RiskInterop.EvaluateGrossExposure(context);

        if (!decision.Approved)
        {
            var reason = decision.Reasons.FirstOrDefault() ?? "Gross exposure limit exceeded.";
            _logger.LogWarning("Gross exposure rule rejected the order; the projected book exceeds the configured ceiling");
            return new MeasuredProjection(RiskValidationResult.Rejected(reason), orderNotional, signedOrderNotional);
        }

        return new MeasuredProjection(RiskValidationResult.Approved(), orderNotional, signedOrderNotional);
    }

    /// <summary>Verdict plus the notional the order consumes, when it could be measured.</summary>
    private readonly record struct MeasuredProjection(
        RiskValidationResult Result,
        decimal? GrossNotional,
        decimal? SignedNotional);
}
