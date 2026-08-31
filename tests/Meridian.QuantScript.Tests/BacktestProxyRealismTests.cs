using Meridian.Backtesting.Sdk;

namespace Meridian.QuantScript.Tests;

/// <summary>
/// Guards the execution-realism configuration surface of <see cref="BacktestProxy"/>.
/// </summary>
/// <remarks>
/// These tests exist because <c>WithFillModel</c> previously stored its argument and never read it:
/// a script asking for order-book execution silently ran the default model and reported results
/// that looked valid. The assertions below pin the two properties that failure violated — a
/// selected setting reaches the engine request, and an unrecognized setting fails loudly.
/// </remarks>
public sealed class BacktestProxyRealismTests
{
    private static BacktestProxy CreateProxy() =>
        new(engine: null, new QuantScriptOptions(), () => CancellationToken.None);

    // ── Fill model selection ─────────────────────────────────────────────────

    [Theory]
    [InlineData("orderbook", ExecutionModel.OrderBook)]
    [InlineData("order-book", ExecutionModel.OrderBook)]
    [InlineData("book", ExecutionModel.OrderBook)]
    [InlineData("midpoint", ExecutionModel.BarMidpoint)]
    [InlineData("barmidpoint", ExecutionModel.BarMidpoint)]
    [InlineData("marketimpact", ExecutionModel.MarketImpact)]
    [InlineData("market-impact", ExecutionModel.MarketImpact)]
    [InlineData("auto", ExecutionModel.Auto)]
    public void WithFillModel_SelectedModel_ReachesTheEngineRequest(string model, ExecutionModel expected)
    {
        var request = CreateProxy().WithFillModel(model).BuildRequest();

        request.DefaultExecutionModel.Should().Be(expected);
    }

    [Theory]
    [InlineData("  OrderBook  ")]
    [InlineData("MIDPOINT")]
    public void WithFillModel_IsCaseAndWhitespaceInsensitive(string model)
    {
        var act = () => CreateProxy().WithFillModel(model);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("orderbok")]
    [InlineData("l3")]
    [InlineData("")]
    [InlineData("   ")]
    public void WithFillModel_UnknownValue_ThrowsRatherThanSilentlyDefaulting(string model)
    {
        var act = () => CreateProxy().WithFillModel(model);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void WithFillModel_Null_Throws()
    {
        var act = () => CreateProxy().WithFillModel((string)null!);

        act.Should().Throw<ArgumentException>();
    }

    // ── Realism passthrough ──────────────────────────────────────────────────

    [Fact]
    public void BuildRequest_CarriesEveryConfiguredRealismSetting()
    {
        var request = CreateProxy()
            .WithFillTiming(FillTiming.SameBar)
            .WithFillConservatism(FillConservatism.Optimistic)
            .WithDelistingPolicy(DelistingPolicy.Hold)
            .WithCommission(BacktestCommissionKind.Percentage, rate: 12m, minimum: 2m, maximum: 50m)
            .WithSlippageBasisPoints(17m)
            .WithParticipationCap(0.25m)
            .WithMarketImpactCoefficient(0.4m)
            .WithOrderBookQueueAheadFraction(0.6m)
            .WithCorporateActionAdjustment(false)
            .WithRiskFreeRate(0.02)
            .BuildRequest();

        request.FillTiming.Should().Be(FillTiming.SameBar);
        request.FillConservatism.Should().Be(FillConservatism.Optimistic);
        request.DelistingPolicy.Should().Be(DelistingPolicy.Hold);
        request.CommissionKind.Should().Be(BacktestCommissionKind.Percentage);
        request.CommissionRate.Should().Be(12m);
        request.CommissionMinimum.Should().Be(2m);
        request.CommissionMaximum.Should().Be(50m);
        request.SlippageBasisPoints.Should().Be(17m);
        request.MaxParticipationRate.Should().Be(0.25m);
        request.MarketImpactCoefficient.Should().Be(0.4m);
        request.OrderBookQueueAheadFraction.Should().Be(0.6m);
        request.AdjustForCorporateActions.Should().BeFalse();
        request.RiskFreeRate.Should().Be(0.02);
    }

    [Fact]
    public void BuildRequest_UnconfiguredProxy_MatchesEngineDefaults()
    {
        // A script that sets nothing must behave exactly as it did before realism was configurable,
        // otherwise this change silently rewrites the results of every existing notebook.
        var reference = new BacktestRequest(From: default, To: default);
        var request = CreateProxy().BuildRequest();

        request.DefaultExecutionModel.Should().Be(reference.DefaultExecutionModel);
        request.FillTiming.Should().Be(reference.FillTiming);
        request.FillConservatism.Should().Be(reference.FillConservatism);
        request.DelistingPolicy.Should().Be(reference.DelistingPolicy);
        request.CommissionKind.Should().Be(reference.CommissionKind);
        request.CommissionRate.Should().Be(reference.CommissionRate);
        request.CommissionMinimum.Should().Be(reference.CommissionMinimum);
        request.CommissionMaximum.Should().Be(reference.CommissionMaximum);
        request.SlippageBasisPoints.Should().Be(reference.SlippageBasisPoints);
        request.MaxParticipationRate.Should().Be(reference.MaxParticipationRate);
        request.MarketImpactCoefficient.Should().Be(reference.MarketImpactCoefficient);
        request.OrderBookQueueAheadFraction.Should().Be(reference.OrderBookQueueAheadFraction);
        request.AdjustForCorporateActions.Should().Be(reference.AdjustForCorporateActions);
        request.RiskFreeRate.Should().Be(reference.RiskFreeRate);
    }

    [Fact]
    public void RealismDescriptor_ReflectsConfiguredSettings()
    {
        var descriptor = CreateProxy()
            .WithFillConservatism(FillConservatism.Optimistic)
            .RealismDescriptor;

        descriptor.FillConservatism.Should().Be(FillConservatism.Optimistic);
    }

    // ── Guard rails ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void WithParticipationCap_OutOfRange_Throws(decimal rate)
    {
        var act = () => CreateProxy().WithParticipationCap(rate);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void WithOrderBookQueueAheadFraction_OutOfRange_Throws(decimal fraction)
    {
        var act = () => CreateProxy().WithOrderBookQueueAheadFraction(fraction);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithCommission_MaximumBelowMinimum_Throws()
    {
        var act = () => CreateProxy().WithCommission(BacktestCommissionKind.PerShare, 1m, minimum: 10m, maximum: 5m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithCommission_NegativeRate_Throws()
    {
        var act = () => CreateProxy().WithCommission(BacktestCommissionKind.PerShare, -1m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithSlippageBasisPoints_Negative_Throws()
    {
        var act = () => CreateProxy().WithSlippageBasisPoints(-1m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithRiskFreeRate_NonFinite_Throws()
    {
        var act = () => CreateProxy().WithRiskFreeRate(double.NaN);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WithoutCommission_SelectsFreeCommission()
    {
        var request = CreateProxy().WithoutCommission().BuildRequest();

        request.CommissionKind.Should().Be(BacktestCommissionKind.Free);
    }
}
