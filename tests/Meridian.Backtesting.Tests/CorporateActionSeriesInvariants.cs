using FluentAssertions;
using FluentAssertions.Execution;
using Meridian.Contracts.SecurityMaster;
using Meridian.TestSupport.CorporateActions;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Invariant assertions over adjusted bar series. These deliberately assert relationships
/// (continuity, piecewise-constant implied factors, notional conservation) rather than point
/// prices, so golden fixtures stay valid even though their prices are synthetic approximations.
/// </summary>
internal static class CorporateActionSeriesInvariants
{
    private const decimal RelativeTolerance = 0.0005m;
    private const decimal FactorTolerance = 0.000001m;

    internal static HistoricalBar ToHistoricalBar(GoldenBar bar, string symbol)
        => new(symbol, bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume, Source: "golden-fixture");

    internal static IReadOnlyList<HistoricalBar> ToHistoricalBars(GoldenCorporateActionScenario scenario)
        => scenario.Bars
            .Select(bar => ToHistoricalBar(bar, scenario.Ticker))
            .OrderBy(static bar => bar.SessionDate)
            .ToList();

    /// <summary>
    /// Day-over-day adjusted returns equal raw returns off ex-dates; across an ex-date the
    /// adjusted return equals the raw return times the declared action's expected multiplier
    /// (splitRatio for price scaling, 1/dividendFactor for cash distributions).
    /// </summary>
    public static void AssertSeriesContinuity(
        IReadOnlyList<HistoricalBar> raw,
        IReadOnlyList<HistoricalBar> adjusted,
        IReadOnlyList<CorporateActionDto> effectiveActions)
    {
        raw.Should().NotBeEmpty();
        adjusted.Should().HaveCount(raw.Count);

        using var scope = new AssertionScope();
        for (var i = 1; i < raw.Count; i++)
        {
            var rawReturn = raw[i].Close / raw[i - 1].Close;
            var adjustedReturn = adjusted[i].Close / adjusted[i - 1].Close;
            var expectedMultiplier = ExpectedReturnMultiplier(
                raw, effectiveActions, raw[i - 1].SessionDate, raw[i].SessionDate);
            var expectedReturn = rawReturn * expectedMultiplier;

            var relativeError = Math.Abs(adjustedReturn - expectedReturn) / expectedReturn;
            relativeError.Should().BeLessThanOrEqualTo(
                RelativeTolerance,
                "adjusted return across {0} -> {1} must equal raw return x {2}",
                raw[i - 1].SessionDate, raw[i].SessionDate, expectedMultiplier);
        }
    }

    /// <summary>
    /// The implied factor (adjusted close / raw close) is piecewise-constant between
    /// effective ex-dates, jumps at each ex-date by exactly the action's factor, and is
    /// exactly 1 on and after the last effective ex-date.
    /// </summary>
    public static void AssertImpliedFactorMonotonicity(
        IReadOnlyList<HistoricalBar> raw,
        IReadOnlyList<HistoricalBar> adjusted,
        IReadOnlyList<CorporateActionDto> effectiveActions)
    {
        raw.Should().NotBeEmpty();
        adjusted.Should().HaveCount(raw.Count);

        var factors = raw
            .Select((bar, i) => adjusted[i].Close / bar.Close)
            .ToArray();

        var lastPriceAffectingExDate = effectiveActions
            .Where(action => IsPriceScaling(action) || IsCashDistribution(action))
            .Select(static action => (DateOnly?)action.ExDate)
            .DefaultIfEmpty(null)
            .Max();

        using var scope = new AssertionScope();
        for (var i = 0; i < raw.Count; i++)
        {
            if (lastPriceAffectingExDate is null || raw[i].SessionDate >= lastPriceAffectingExDate.Value)
            {
                Math.Abs(factors[i] - 1m).Should().BeLessThanOrEqualTo(
                    FactorTolerance,
                    "bars on/after the last effective ex-date must be unadjusted ({0})", raw[i].SessionDate);
            }

            if (i == 0)
                continue;

            var jump = ExpectedReturnMultiplier(raw, effectiveActions, raw[i - 1].SessionDate, raw[i].SessionDate);
            if (jump == 1m)
            {
                var drift = Math.Abs(factors[i] - factors[i - 1]) / factors[i - 1];
                drift.Should().BeLessThanOrEqualTo(
                    FactorTolerance,
                    "implied factor must be constant with no ex-date between {0} and {1}",
                    raw[i - 1].SessionDate, raw[i].SessionDate);
            }
            else
            {
                // factor[i-1] / factor[i] == 1 / jump (jump = splitRatio / divFactor).
                var observed = factors[i - 1] / factors[i];
                var expected = 1m / jump;
                var relativeError = Math.Abs(observed - expected) / expected;
                relativeError.Should().BeLessThanOrEqualTo(
                    RelativeTolerance,
                    "implied factor jump across the ex-date between {0} and {1} must match the declared action",
                    raw[i - 1].SessionDate, raw[i].SessionDate);
            }
        }
    }

    /// <summary>
    /// Split-only scenarios: close x volume is conserved per bar within volume-rounding
    /// tolerance (prices divide by the ratio, volume multiplies by it).
    /// </summary>
    public static void AssertNotionalConservation(
        IReadOnlyList<HistoricalBar> raw,
        IReadOnlyList<HistoricalBar> adjusted)
    {
        adjusted.Should().HaveCount(raw.Count);

        using var scope = new AssertionScope();
        for (var i = 0; i < raw.Count; i++)
        {
            var rawNotional = raw[i].Close * raw[i].Volume;
            var adjustedNotional = adjusted[i].Close * adjusted[i].Volume;
            var relativeError = Math.Abs(adjustedNotional - rawNotional) / rawNotional;
            relativeError.Should().BeLessThanOrEqualTo(
                0.0001m,
                "split adjustment must conserve close x volume on {0}", raw[i].SessionDate);
        }
    }

    /// <summary>
    /// Computes the multiplier the adjusted return should carry relative to the raw return
    /// for the interval (from, to], mirroring the adjustment service's math: splits multiply
    /// by the ratio, dividend groups divide by (1 - totalDiv / prevClose).
    /// </summary>
    internal static decimal ExpectedReturnMultiplier(
        IReadOnlyList<HistoricalBar> raw,
        IReadOnlyList<CorporateActionDto> effectiveActions,
        DateOnly fromExclusive,
        DateOnly toInclusive)
    {
        var multiplier = 1m;

        foreach (var action in effectiveActions)
        {
            if (action.ExDate <= fromExclusive || action.ExDate > toInclusive)
                continue;

            if (IsPriceScaling(action))
                multiplier *= action.SplitRatio!.Value;
        }

        foreach (var dividendGroup in effectiveActions
                     .Where(action => action.ExDate > fromExclusive && action.ExDate <= toInclusive)
                     .Where(IsCashDistribution)
                     .GroupBy(static action => action.ExDate))
        {
            var previousClose = raw
                .Where(bar => bar.SessionDate < dividendGroup.Key)
                .Select(static bar => (decimal?)bar.Close)
                .LastOrDefault();

            if (previousClose is not > 0m)
                continue;

            var factor = 1m - (dividendGroup.Sum(static action => action.DividendPerShare!.Value) / previousClose.Value);
            if (factor is > 0m and <= 1m)
                multiplier /= factor;
        }

        return multiplier;
    }

    private static bool IsPriceScaling(CorporateActionDto action)
        => CorporateActionTypeDescriptorCatalog.TryNormalize(action.EventType, out var descriptor)
           && descriptor.AdjustmentBehavior == CorporateActionAdjustmentBehavior.PriceScaling
           && action.SplitRatio is > 0m;

    private static bool IsCashDistribution(CorporateActionDto action)
        => CorporateActionTypeDescriptorCatalog.TryNormalize(action.EventType, out var descriptor)
           && descriptor.AdjustmentBehavior == CorporateActionAdjustmentBehavior.CashDistribution
           && action.DividendPerShare is > 0m;
}
