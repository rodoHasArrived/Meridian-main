using FluentAssertions;
using Meridian.Backtesting.Sdk.Strategies.AdvancedCarry;

namespace Meridian.Backtesting.Tests;

public sealed class AdvancedCarryDecisionEngineTests
{
    [Fact]
    public void BuildDecision_MeanVariance_OverweightsHigherExpectedReturnAssetsWithinRiskCap()
    {
        var engine = new AdvancedCarryDecisionEngine();
        var assets = CreateAssets(
            ("AAA", 0.09, 0.05, 0.04, 0.10, 8, 250_000),
            ("BBB", 0.07, 0.05, 0.02, 0.10, 8, 250_000),
            ("CCC", 0.05, 0.05, 0.00, 0.10, 8, 250_000));

        var decision = engine.BuildDecision(
            new AdvancedCarryInput(
                assets,
                new CarryPortfolioState(100_000m),
                [
                    new AssetCorrelation("AAA", "BBB", 0.30),
                    new AssetCorrelation("AAA", "CCC", 0.30),
                    new AssetCorrelation("BBB", "CCC", 0.30),
                ]),
            new AdvancedCarryConfiguration(
                CarryOptimizationMethod.MeanVariance,
                Risk: new AdvancedCarryRiskOptions(MaxSinglePosition: 0.50, TargetVolatility: null)));

        decision.TargetWeights.Should().HaveCount(3);
        decision.TargetWeights.Sum(weight => weight.Weight).Should().BeApproximately(1.0, 1e-6);
        decision.TargetWeights.First().Symbol.Should().Be("AAA");
        decision.TargetWeights.First().Weight.Should().BeGreaterThan(0.38);
        decision.TargetWeights.Should().BeInDescendingOrder(weight => weight.Weight);
    }

    [Fact]
    public void BuildDecision_RiskParity_ProducesNearEqualRiskContributions()
    {
        var engine = new AdvancedCarryDecisionEngine();
        var decision = engine.BuildDecision(
            new AdvancedCarryInput(
                CreateAssets(
                    ("LOW", 0.06, 0.05, 0.01, 0.08, 8, 250_000),
                    ("MID", 0.06, 0.05, 0.01, 0.12, 8, 250_000),
                    ("HIGH", 0.06, 0.05, 0.01, 0.16, 8, 250_000)),
                new CarryPortfolioState(100_000m),
                [
                    new AssetCorrelation("LOW", "MID", 0.0),
                    new AssetCorrelation("LOW", "HIGH", 0.0),
                    new AssetCorrelation("MID", "HIGH", 0.0),
                ]),
            new AdvancedCarryConfiguration(
                CarryOptimizationMethod.RiskParity,
                Risk: new AdvancedCarryRiskOptions(MaxSinglePosition: 0.80, TargetVolatility: null)));

        var contributions = decision.RiskReport.RiskContributions.Values.ToArray();
        (contributions.Max() - contributions.Min()).Should().BeLessThan(0.03);
        decision.TargetWeights.Single(weight => weight.Symbol == "LOW").Weight
            .Should().BeGreaterThan(decision.TargetWeights.Single(weight => weight.Symbol == "HIGH").Weight);
    }

    [Fact]
    public void BuildDecision_WithKellySizing_LeavesCashBufferWhenCapsBind()
    {
        var engine = new AdvancedCarryDecisionEngine();
        var decision = engine.BuildDecision(
            new AdvancedCarryInput(
                CreateAssets(
                    ("AAA", 0.12, 0.07, 0.05, 0.20, 8, 250_000),
                    ("BBB", 0.11, 0.07, 0.04, 0.18, 8, 250_000),
                    ("CCC", 0.10, 0.07, 0.03, 0.17, 8, 250_000)),
                new CarryPortfolioState(100_000m)),
            new AdvancedCarryConfiguration(
                CarryOptimizationMethod.MaximumSharpe,
                Risk: new AdvancedCarryRiskOptions(
                    MaxSinglePosition: 0.50,
                    TargetVolatility: null,
                    UseKellySizing: true,
                    KellyFraction: 0.25,
                    MaxKellyPosition: 0.12)));

        decision.CashWeight.Should().BeGreaterThan(0.50);
        decision.TargetWeights.All(weight => weight.Weight <= 0.12 + 1e-6).Should().BeTrue();
    }

    [Fact]
    public void BuildDecision_BuildsTailRiskScenariosAndExecutionPlans()
    {
        var engine = new AdvancedCarryDecisionEngine();
        var assets = CreateAssets(
            ("AAA", 0.08, 0.05, 0.03, 0.10, 6, 500_000),
            ("BBB", 0.07, 0.05, 0.02, 0.11, 42, 40_000));

        var input = new AdvancedCarryInput(
            assets,
            new CarryPortfolioState(
                100_000m,
                new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
                {
                    ["AAA"] = 100,
                    ["BBB"] = 0
                }),
            [
                new AssetCorrelation("AAA", "BBB", 0.65)
            ],
            new DateTimeOffset(2026, 4, 1, 14, 0, 0, TimeSpan.Zero));

        var decision = engine.BuildDecision(
            input,
            new AdvancedCarryConfiguration(
                CarryOptimizationMethod.MeanVariance,
                Risk: new AdvancedCarryRiskOptions(MaxSinglePosition: 0.70, TargetVolatility: null),
                Execution: new AdvancedCarryExecutionOptions(
                    Algorithm: CarryExecutionAlgorithm.Adaptive,
                    DurationMinutes: 20,
                    SliceCount: 4,
                    MaxSliceQuantity: 250,
                    UseIcebergOrders: true,
                    IcebergDisplayQuantity: 50)));

        decision.RiskReport.Scenarios.Should().HaveCount(4);
        decision.RiskReport.HistoricalTailRisk.Amount.Should().BeGreaterThan(0m);
        decision.RiskReport.ParametricTailRisk.Amount.Should().BeGreaterThan(0m);
        decision.ExecutionPlans.Should().NotBeEmpty();

        var wideSpreadPlan = decision.ExecutionPlans.Single(plan => plan.Symbol == "BBB");
        wideSpreadPlan.Algorithm.Should().Be(CarryExecutionAlgorithm.Pov);
        wideSpreadPlan.Slices.Sum(slice => slice.Quantity).Should().Be(wideSpreadPlan.TotalQuantity);
        wideSpreadPlan.Slices.All(slice => slice.DisplayQuantity <= 50).Should().BeTrue();
    }

    private static IReadOnlyList<CarryAssetSnapshot> CreateAssets(params (string Symbol, double ExpectedReturn, double CarryYield, double PriceReturn, double Volatility, double SpreadBps, double Adv)[] inputs)
    {
        const int historyLength = 30;
        return inputs.Select((input, index) =>
        {
            var history = Enumerable.Range(0, historyLength)
                .Select(day => ((index + 1) * 0.0005) + ((day % 5) - 2) * 0.0002)
                .ToArray();

            return new CarryAssetSnapshot(
                input.Symbol,
                LastPrice: 25m + index,
                ExpectedAnnualReturn: input.ExpectedReturn,
                AnnualCarryYield: input.CarryYield,
                AnnualPriceReturn: input.PriceReturn,
                AnnualVolatility: input.Volatility,
                DurationYears: 4.0 + index,
                SpreadDurationYears: 3.5 + index,
                AverageDailyVolume: input.Adv,
                BidAskSpreadBps: input.SpreadBps,
                MarketBeta: 0.45 + (index * 0.05),
                HistoricalDailyReturns: history);
        }).ToList();
    }
}
