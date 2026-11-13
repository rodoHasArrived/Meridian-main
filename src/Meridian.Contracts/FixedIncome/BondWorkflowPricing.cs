namespace Meridian.Contracts.FixedIncome;

/// <summary>A signed future payment and its positive, settlement-rebased discount factor.</summary>
public sealed record BondWorkflowDiscountedFlow(decimal Amount, decimal TimeInYears, decimal DiscountFactor);

/// <summary>Signed cash sensitivity to a one basis point continuous-rate increase; normalized measures may be unavailable.</summary>
public sealed record BondWorkflowRisk(decimal PresentValue, decimal Dv01, decimal? Duration, decimal? Convexity);

/// <summary>
/// Signed-payment pricing for the bond workflow. This arithmetic deliberately leaves retained
/// bond-pricing-math-v1 untouched. Negative coupon obligations and discount factors above one
/// are admitted; no coupon, value or sensitivity is implicitly floored.
/// </summary>
public static class BondWorkflowPricing
{
    public const string ModelVersion = "bond-workflow-signed-pricing-v1";
    private const decimal Bump = 0.0001m;
    private const int MaxFlows = 1200;

    public static decimal PresentValue(IReadOnlyList<BondWorkflowDiscountedFlow> flows, decimal continuousSpread = 0m)
    {
        Validate(flows);
        try
        {
            return flows.Aggregate(0m, (total, flow) => checked(total + Discount(flow, continuousSpread)));
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException("CalculationBoundsExceeded: signed present value exceeds decimal bounds.", exception);
        }
    }

    public static BondWorkflowRisk Risk(IReadOnlyList<BondWorkflowDiscountedFlow> flows, decimal continuousSpread = 0m)
    {
        var value = PresentValue(flows, continuousSpread);
        var down = PresentValue(flows, checked(continuousSpread - Bump));
        var up = PresentValue(flows, checked(continuousSpread + Bump));
        var dv01 = down / 2m - up / 2m;
        // Scale each absolute contribution before summing: a hedged portfolio can have a
        // representable net value even when its unscaled gross exceeds decimal.MaxValue.
        var scaledGross = flows.Aggregate(0m, (total, flow) => checked(total
            + Math.Abs(Discount(flow, continuousSpread)) * 0.000000000001m));
        var cancellationThreshold = Math.Max(0.000000000001m, scaledGross);
        if (Math.Abs(value) <= cancellationThreshold)
            return new(value, dv01, null, null);
        decimal? duration = null;
        decimal? convexity = null;
        try
        { duration = dv01 / Bump / value; }
        catch (OverflowException) { /* Absolute sensitivity remains usable. */ }
        try
        { convexity = ((up - value) + (down - value)) / Bump / Bump / value; }
        catch (OverflowException) { /* Near-cancellation can make normalized measures unrepresentable. */ }
        return new(value, dv01, duration, convexity);
    }

    /// <summary>
    /// Solves sum(cash * discount * exp(-spread * years)) = dirtyCash. Exactly one sign
    /// change in the time-ordered coefficients, including initial -dirtyCash, certifies a
    /// globally unique finite root when one can be bracketed. Other patterns are not assigned
    /// an arbitrary root, even when a local numerical solver could find one.
    /// </summary>
    public static bool TrySolveSpread(IReadOnlyList<BondWorkflowDiscountedFlow> flows, decimal dirtyCash,
        out decimal spread, out string? issue)
    {
        spread = 0m;
        issue = null;
        try
        { Validate(flows); }
        catch (ArgumentException) { issue = "InvalidCashFlows"; return false; }

        List<Coefficient> coefficients;
        try
        {
            coefficients = flows.GroupBy(flow => flow.TimeInYears)
                .Select(group => new Coefficient(group.Key, group.Aggregate(0m,
                    (total, flow) => checked(total + flow.Amount * flow.DiscountFactor))))
                .Append(new Coefficient(0m, checked(-dirtyCash)))
                .Where(coefficient => coefficient.Amount != 0m)
                .OrderBy(coefficient => coefficient.Time).ToList();
        }
        catch (OverflowException) { issue = "CalculationBoundsExceeded"; return false; }

        var changes = 0;
        for (var index = 1; index < coefficients.Count; index++)
            if (Math.Sign(coefficients[index].Amount) != Math.Sign(coefficients[index - 1].Amount))
                changes++;
        if (changes != 1)
        {
            issue = changes == 0 ? "YieldNoRoot" : "YieldUniquenessNotEstablished";
            return false;
        }

        // A signed logarithmic sum keeps the bracketing sign finite at extreme trial rates.
        // Sign-based bisection needs no assumption that the untransformed PV is monotonic.
        var low = -1d;
        var high = 1d;
        var lowSign = EvaluateSign(coefficients, low);
        var highSign = EvaluateSign(coefficients, high);
        for (var expansion = 0; lowSign == highSign && expansion < 24; expansion++)
        {
            low *= 2d;
            high *= 2d;
            lowSign = EvaluateSign(coefficients, low);
            highSign = EvaluateSign(coefficients, high);
        }
        if (lowSign == highSign)
        {
            issue = "YieldNotBracketed";
            return false;
        }
        var candidate = lowSign == 0 ? low : highSign == 0 ? high : (low + high) / 2d;
        if (lowSign != 0 && highSign != 0)
            for (var iteration = 0; iteration < 256; iteration++)
            {
                candidate = (low + high) / 2d;
                var sign = EvaluateSign(coefficients, candidate);
                if (sign == 0 || high - low <= Math.Max(1e-14d, Math.Abs(candidate) * 1e-14d))
                    break;
                if (sign == lowSign)
                    low = candidate;
                else
                    high = candidate;
            }
        try
        {
            spread = decimal.Round((decimal)candidate, 12, MidpointRounding.AwayFromZero);
            var value = PresentValue(flows, spread);
            if (Math.Abs(value - dirtyCash) <= Math.Max(0.00000001m, Math.Abs(dirtyCash) * 0.000000001m))
                return true;
        }
        catch (Exception exception) when (exception is ArithmeticException or InvalidOperationException)
        {
            issue = "CalculationBoundsExceeded";
        }
        spread = 0m;
        issue ??= "YieldResidualExceeded";
        return false;
    }

    private static decimal Discount(BondWorkflowDiscountedFlow flow, decimal spread)
    {
        if (flow.Amount == 0m)
            return 0m;
        var factor = Math.Exp(-(double)spread * (double)flow.TimeInYears);
        if (!double.IsFinite(factor) || factor == 0d || factor > (double)decimal.MaxValue)
            throw new InvalidOperationException("CalculationBoundsExceeded: continuous discount factor is not representable.");
        return checked(flow.Amount * flow.DiscountFactor * (decimal)factor);
    }

    private static int EvaluateSign(IReadOnlyList<Coefficient> coefficients, double spread)
    {
        var logs = coefficients.Select(coefficient =>
            Math.Log((double)Math.Abs(coefficient.Amount)) - (double)coefficient.Time * spread).ToArray();
        var maximum = logs.Max();
        var sum = 0d;
        var compensation = 0d;
        for (var index = 0; index < coefficients.Count; index++)
        {
            var term = Math.Sign(coefficients[index].Amount) * Math.Exp(logs[index] - maximum) - compensation;
            var next = sum + term;
            compensation = (next - sum) - term;
            sum = next;
        }
        return Math.Sign(sum);
    }

    private static void Validate(IReadOnlyList<BondWorkflowDiscountedFlow> flows)
    {
        ArgumentNullException.ThrowIfNull(flows);
        if (flows.Count is 0 or > MaxFlows || flows.Any(flow => flow is null || flow.TimeInYears <= 0m || flow.DiscountFactor <= 0m))
            throw new ArgumentException("Signed pricing needs one to 1200 future cash flows with positive times and discount factors.", nameof(flows));
    }

    private sealed record Coefficient(decimal Time, decimal Amount);
}
