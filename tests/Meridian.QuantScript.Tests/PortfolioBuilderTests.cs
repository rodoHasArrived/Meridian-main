using Meridian.QuantScript.Tests.Helpers;

namespace Meridian.QuantScript.Tests;

public sealed class PortfolioBuilderTests
{
    // ── EqualWeight ──────────────────────────────────────────────────────────

    [Fact]
    public void EqualWeight_TwoAssets_EachWeightIsHalf()
    {
        var s1 = TestPriceSeriesBuilder.Build("A", 30);
        var s2 = TestPriceSeriesBuilder.Build("B", 30);

        var result = PortfolioBuilder.EqualWeight(s1, s2);

        result.Weights["A"].Should().BeApproximately(0.5, 1e-10);
        result.Weights["B"].Should().BeApproximately(0.5, 1e-10);
    }

    [Fact]
    public void EqualWeight_EmptySeries_Throws()
    {
        var act = () => PortfolioBuilder.EqualWeight();
        act.Should().Throw<ArgumentException>();
    }

    // ── EfficientFrontier – basic feasibility ────────────────────────────────

    [Fact]
    public void EfficientFrontier_SingleAsset_ReturnsWeightOfOne()
    {
        var s = TestPriceSeriesBuilder.Build("SPY", 60);
        var constraints = new EfficientFrontierConstraints { TargetReturn = 0.0 };

        var result = PortfolioBuilder.EfficientFrontier(constraints, s);

        result.Weights["SPY"].Should().BeApproximately(1.0, 1e-10);
    }

    [Fact]
    public void EfficientFrontier_WeightsSumToOne()
    {
        var s1 = TestPriceSeriesBuilder.Build("A", 100);
        var s2 = TestPriceSeriesBuilder.Build("B", 100);
        var s3 = TestPriceSeriesBuilder.Build("C", 100);
        var constraints = new EfficientFrontierConstraints { TargetReturn = 0.0 };

        var result = PortfolioBuilder.EfficientFrontier(constraints, s1, s2, s3);

        var sum = result.Weights.Values.Sum();
        sum.Should().BeApproximately(1.0, 1e-6);
    }

    [Fact]
    public void EfficientFrontier_WeightsRespectDefaultBounds()
    {
        var s1 = TestPriceSeriesBuilder.Build("A", 80);
        var s2 = TestPriceSeriesBuilder.Build("B", 80);
        var constraints = new EfficientFrontierConstraints { TargetReturn = 0.0 };

        var result = PortfolioBuilder.EfficientFrontier(constraints, s1, s2);

        foreach (var w in result.Weights.Values)
        {
            w.Should().BeGreaterThanOrEqualTo(0.0 - 1e-9);
            w.Should().BeLessThanOrEqualTo(1.0 + 1e-9);
        }
    }

    [Fact]
    public void EfficientFrontier_WeightsRespectCustomMinMax()
    {
        var s1 = TestPriceSeriesBuilder.Build("A", 80);
        var s2 = TestPriceSeriesBuilder.Build("B", 80);
        var s3 = TestPriceSeriesBuilder.Build("C", 80);
        var constraints = new EfficientFrontierConstraints
        {
            TargetReturn = 0.0,
            MinWeight = 0.1,
            MaxWeight = 0.6
        };

        var result = PortfolioBuilder.EfficientFrontier(constraints, s1, s2, s3);

        foreach (var w in result.Weights.Values)
        {
            w.Should().BeGreaterThanOrEqualTo(0.1 - 1e-6);
            w.Should().BeLessThanOrEqualTo(0.6 + 1e-6);
        }
    }

    [Fact]
    public void EfficientFrontier_EmptySeries_Throws()
    {
        var constraints = new EfficientFrontierConstraints { TargetReturn = 0.0 };
        var act = () => PortfolioBuilder.EfficientFrontier(constraints);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EfficientFrontier_NullConstraints_Throws()
    {
        var s = TestPriceSeriesBuilder.Build("X", 10);
        var act = () => PortfolioBuilder.EfficientFrontier(null!, s);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── EfficientFrontier – return target ────────────────────────────────────

    [Fact]
    public void EfficientFrontier_ZeroTargetReturn_WeightsSumToOne()
    {
        // Two assets with different return profiles — any feasible solution must still budget-balance.
        var s1 = TestPriceSeriesBuilder.Build("HIGH", 120, startPrice: 100m);
        var s2 = TestPriceSeriesBuilder.Build("LOW", 120, startPrice: 50m);
        var constraints = new EfficientFrontierConstraints { TargetReturn = 0.0 };

        var result = PortfolioBuilder.EfficientFrontier(constraints, s1, s2);

        result.Weights.Values.Sum().Should().BeApproximately(1.0, 1e-6);
    }

    [Fact]
    public void EfficientFrontier_FlatSeries_FallsBackToFeasibleWeights()
    {
        // Flat price series → zero variance and zero return.  The optimizer must still return
        // a valid weight vector (budget = 1, within bounds).
        var s1 = TestPriceSeriesBuilder.BuildFlat("FLAT1", 50);
        var s2 = TestPriceSeriesBuilder.BuildFlat("FLAT2", 50);
        var constraints = new EfficientFrontierConstraints { TargetReturn = 0.0 };

        var result = PortfolioBuilder.EfficientFrontier(constraints, s1, s2);

        result.Weights.Values.Sum().Should().BeApproximately(1.0, 1e-6);
        foreach (var w in result.Weights.Values)
        {
            w.Should().BeGreaterThanOrEqualTo(0.0 - 1e-9);
            w.Should().BeLessThanOrEqualTo(1.0 + 1e-9);
        }
    }

    // ── EfficientFrontier – result portfolio analytics ───────────────────────

    [Fact]
    public void EfficientFrontier_ReturnsPortfolioWithCorrectSymbols()
    {
        var s1 = TestPriceSeriesBuilder.Build("AAA", 60);
        var s2 = TestPriceSeriesBuilder.Build("BBB", 60);
        var constraints = new EfficientFrontierConstraints { TargetReturn = 0.0 };

        var result = PortfolioBuilder.EfficientFrontier(constraints, s1, s2);

        result.Symbols.Should().BeEquivalentTo(["AAA", "BBB"]);
    }

    [Fact]
    public void EfficientFrontier_ReturnedPortfolioProducesReturnSeries()
    {
        var s1 = TestPriceSeriesBuilder.Build("X", 60);
        var s2 = TestPriceSeriesBuilder.Build("Y", 60);
        var constraints = new EfficientFrontierConstraints { TargetReturn = 0.0 };

        var result = PortfolioBuilder.EfficientFrontier(constraints, s1, s2);
        var returns = result.Returns();

        returns.Should().NotBeNull();
        returns.Symbol.Should().Be("Portfolio");
    }

    [Fact]
    public void EfficientFrontier_PortfolioReturnMeetsTargetWhenFeasible()
    {
        // Use a deterministic ascending series so mean daily return is positive and known.
        var s1 = TestPriceSeriesBuilder.Build("HI", 120, startPrice: 100m);
        var s2 = TestPriceSeriesBuilder.Build("LO", 120, startPrice: 50m);

        // Ask for a very small positive target (well within achievable range).
        const double target = 0.0005;
        var constraints = new EfficientFrontierConstraints { TargetReturn = target };

        var result = PortfolioBuilder.EfficientFrontier(constraints, s1, s2);

        // Compute realised portfolio daily return as weighted sum of asset means.
        var portReturn = result.Weights.Sum(kv =>
        {
            var series = new[] { s1, s2 }.First(s => s.Symbol == kv.Key);
            var mean = series.DailyReturns().Points.Average(p => p.Value);
            return kv.Value * mean;
        });

        portReturn.Should().BeGreaterThanOrEqualTo(target - 1e-4,
            "portfolio return should meet the target within numerical tolerance");
    }
}
