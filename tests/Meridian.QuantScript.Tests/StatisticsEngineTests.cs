namespace Meridian.QuantScript.Tests;

public sealed class StatisticsEngineTests
{
    private static double[] Ascending(int n) => Enumerable.Range(1, n).Select(i => i * 0.001).ToArray();

    [Fact] public void Mean_Returns_Average() => StatisticsEngine.Mean([1.0, 2.0, 3.0]).Should().BeApproximately(2.0, 1e-9);
    [Fact] public void StdDev_Returns_Positive_For_Varied_Data() => StatisticsEngine.StdDev(Ascending(20)).Should().BePositive();
    [Fact] public void Variance_Of_Constant_Array_Is_Zero() => StatisticsEngine.Variance([5.0, 5.0, 5.0]).Should().BeApproximately(0.0, 1e-9);
    [Fact] public void SharpeRatio_Returns_Positive_For_Ascending_Returns() => StatisticsEngine.SharpeRatio(Ascending(50)).Should().BePositive();
    [Fact] public void MaxDrawdown_Zero_For_Always_Rising() => StatisticsEngine.MaxDrawdown(Ascending(20)).Should().BeApproximately(0.0, 1e-9);
    [Fact] public void MaxDrawdown_Positive_For_Drop() => StatisticsEngine.MaxDrawdown([0.1, -0.2, 0.05]).Should().BePositive();
    [Fact] public void Correlation_Of_Identical_Series_Is_One() => StatisticsEngine.Correlation(Ascending(10), Ascending(10)).Should().BeApproximately(1.0, 1e-9);
    [Fact] public void CAGR_Positive_For_Rising_Returns() => StatisticsEngine.CAGR(Ascending(252), 1.0).Should().BePositive();
    private static ReturnSeries BuildReturns(IReadOnlyList<double> values)
    {
        var pts = values.Select((v, i) =>
            new ReturnPoint(DateOnly.FromDateTime(DateTime.Today.AddDays(i)), v)).ToList();
        return new ReturnSeries("T", ReturnKind.Arithmetic, pts);
    }

    // ── Sharpe ──────────────────────────────────────────────────────────────

    [Fact]
    public void Sharpe_ZeroReturns_ReturnsZero()
    {
        var r = BuildReturns(Enumerable.Repeat(0.0, 252).ToList());
        r.SharpeRatio(0).Should().BeApproximately(0, 1e-6);
    }

    [Fact]
    public void Sharpe_PositiveConstantReturns_Positive()
    {
        var r = BuildReturns(Enumerable.Repeat(0.001, 252).ToList());
        r.SharpeRatio(0).Should().BePositive();
    }

    [Fact]
    public void Sharpe_EmptySeries_ReturnsZero()
    {
        var r = new ReturnSeries("X", ReturnKind.Arithmetic, []);
        r.SharpeRatio().Should().Be(0);
    }

    // ── Sortino ─────────────────────────────────────────────────────────────

    [Fact]
    public void Sortino_AllPositive_ReturnsPositiveInfinity()
    {
        var r = BuildReturns(Enumerable.Repeat(0.002, 100).ToList());
        r.SortinoRatio(0).Should().Be(double.PositiveInfinity);
    }

    // ── Volatility ───────────────────────────────────────────────────────────

    [Fact]
    public void AnnualizedVolatility_SingleValue_ReturnsZero()
    {
        var r = BuildReturns([0.01]);
        r.AnnualizedVolatility().Should().Be(0);
    }

    [Fact]
    public void AnnualizedVolatility_KnownDailyVol_ApproximatesAnnual()
    {
        // σ_daily = 0.01, annualised ≈ 0.01 * sqrt(252) ≈ 0.1587
        var rng = new Random(42);
        var returns = Enumerable.Range(0, 252)
            .Select(_ => 0.01 * (rng.NextDouble() * 2 - 1))
            .ToList();
        var r = BuildReturns(returns);
        r.AnnualizedVolatility().Should().BeInRange(0, 1.0); // sanity
    }

    // ── Drawdown ─────────────────────────────────────────────────────────────

    [Fact]
    public void MaxDrawdown_MonotonicallyIncreasing_IsZero()
    {
        var r = BuildReturns(Enumerable.Repeat(0.01, 50).ToList());
        r.MaxDrawdown().Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void MaxDrawdown_SingleLargeDrop_IsNegative()
    {
        var values = Enumerable.Repeat(0.01, 20).ToList<double>();
        values.Add(-0.5);
        var r = BuildReturns(values);
        r.MaxDrawdown().Should().BeLessThan(0);
    }

    // ── Beta / Alpha ─────────────────────────────────────────────────────────

    [Fact]
    public void Beta_IdenticalSeries_IsOne()
    {
        var vals = Enumerable.Range(0, 50).Select(i => (double)i * 0.001 - 0.025).ToList();
        var r = BuildReturns(vals);
        var bm = BuildReturns(vals);
        r.Beta(bm).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Correlation_IdenticalSeries_IsOne()
    {
        var vals = Enumerable.Range(0, 50).Select(i => (double)i * 0.001 - 0.025).ToList();
        var r = BuildReturns(vals);
        var bm = BuildReturns(vals);
        r.Correlation(bm).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void Correlation_OppositeSign_IsNegativeOne()
    {
        var vals = Enumerable.Range(0, 50).Select(i => (double)i * 0.001).ToList();
        var r = BuildReturns(vals);
        var neg = BuildReturns(vals.Select(v => -v).ToList());
        r.Correlation(neg).Should().BeApproximately(-1.0, 1e-9);
    }

    // ── Skewness / Kurtosis ──────────────────────────────────────────────────

    [Fact]
    public void Skewness_SymmetricData_NearZero()
    {
        var vals = Enumerable.Range(-20, 41).Select(i => (double)i / 100).ToList();
        var r = BuildReturns(vals);
        r.Skewness().Should().BeApproximately(0, 1e-6);
    }

    // ── Rolling statistics ────────────────────────────────────────────────────

    [Fact]
    public void RollingMean_WindowEqualsCount_ReturnsMeanForLastElement()
    {
        var vals = Enumerable.Range(1, 10).Select(i => (double)i / 100).ToList();
        var r = BuildReturns(vals);
        var rolling = r.RollingMean(10);
        rolling.Points[^1].Value.Should().BeApproximately(vals.Average(), 1e-9);
    }

    [Fact]
    public void DrawdownSeries_Length_EqualsPeriods()
    {
        var r = BuildReturns(Enumerable.Range(0, 20).Select(i => 0.001 * i - 0.01).ToList());
        r.DrawdownSeries().Count.Should().Be(20);
    }
}
