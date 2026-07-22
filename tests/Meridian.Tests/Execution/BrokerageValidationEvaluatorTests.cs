using FluentAssertions;
using Meridian.Execution.Sdk;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Guards the live-readiness review evaluator. The failure mode under guard: a lane is
/// declared live-ready while the brokerage configuration still points at paper trading,
/// has live execution disabled, or carries no pre-trade guardrails — letting a go-live
/// claim pass review against a broker that cannot actually route production orders.
/// </summary>
public sealed class BrokerageValidationEvaluatorTests
{
    [Fact]
    public void Evaluate_NullConfiguration_ReportsRequiredWithUnconfiguredGateway()
    {
        var report = BrokerageValidationEvaluator.Evaluate(configuration: null);

        report.State.Should().Be(BrokerageValidationState.Required);
        report.HasBlockingGap.Should().BeFalse();
        report.GatewayId.Should().BeEmpty();
        report.GatewayDisplayName.Should().Be("Unconfigured");
        report.Findings.Should().BeEmpty();
        report.Summary.Should().Contain("not projected into this workflow");
    }

    [Fact]
    public void Evaluate_LiveExecutionDisabled_ReportsBlockingGap()
    {
        var configuration = CreateLiveConfiguration("alpaca");
        configuration.LiveExecutionEnabled = false;

        var report = BrokerageValidationEvaluator.Evaluate(configuration);

        report.State.Should().Be(BrokerageValidationState.Gap);
        report.HasBlockingGap.Should().BeTrue();
        report.Findings.Should().ContainSingle()
            .Which.Should().Contain("does not enable live execution");
        report.Summary.Should().Contain("Resolve the Alpaca broker configuration");
    }

    [Fact]
    public void Evaluate_BlankGateway_ReportsGapAskingForLiveBroker()
    {
        var configuration = CreateLiveConfiguration(gateway: "   ");

        var report = BrokerageValidationEvaluator.Evaluate(configuration);

        report.State.Should().Be(BrokerageValidationState.Gap);
        report.GatewayId.Should().BeEmpty();
        report.GatewayDisplayName.Should().Be("Unconfigured");
        report.Findings.Should().ContainSingle()
            .Which.Should().Contain("No live brokerage gateway is configured");
        report.Summary.Should().Contain("Configure a live broker");
    }

    [Fact]
    public void Evaluate_GatewayStillPaper_ReportsGap()
    {
        var configuration = CreateLiveConfiguration("paper");

        var report = BrokerageValidationEvaluator.Evaluate(configuration);

        report.State.Should().Be(BrokerageValidationState.Gap);
        report.GatewayDisplayName.Should().Be("Paper trading");
        report.Findings.Should().ContainSingle()
            .Which.Should().Contain("still points to paper trading");
    }

    [Fact]
    public void Evaluate_LiveDisabledAndPaperGateway_AccumulatesBothFindings()
    {
        var configuration = new BrokerageConfiguration
        {
            Gateway = "paper",
            LiveExecutionEnabled = false
        };

        var report = BrokerageValidationEvaluator.Evaluate(configuration);

        report.State.Should().Be(BrokerageValidationState.Gap);
        report.Findings.Should().HaveCount(2,
            "a reviewer must see every blocking gap at once, not one per review round");
        report.Summary.Should()
            .Contain("does not enable live execution").And
            .Contain("still points to paper trading");
    }

    [Fact]
    public void Evaluate_LiveGatewayWithoutGuardrails_WarnsAboutMissingLimits()
    {
        var configuration = CreateLiveConfiguration("alpaca");

        var report = BrokerageValidationEvaluator.Evaluate(configuration);

        report.State.Should().Be(BrokerageValidationState.Required);
        report.HasBlockingGap.Should().BeFalse();
        report.Findings.Should().ContainSingle()
            .Which.Should().Contain("MaxPositionSize, MaxOrderNotional, or MaxOpenOrders");
        report.Summary.Should().Contain("No explicit MaxPositionSize");
    }

    [Theory]
    [InlineData(5_000, 0, 0)]
    [InlineData(0, 250_000, 0)]
    [InlineData(0, 0, 25)]
    public void Evaluate_AnyExplicitGuardrail_ReportsCleanRequiredReview(
        decimal maxPositionSize, decimal maxOrderNotional, int maxOpenOrders)
    {
        var configuration = CreateLiveConfiguration("alpaca");
        configuration.MaxPositionSize = maxPositionSize;
        configuration.MaxOrderNotional = maxOrderNotional;
        configuration.MaxOpenOrders = maxOpenOrders;

        var report = BrokerageValidationEvaluator.Evaluate(configuration);

        report.State.Should().Be(BrokerageValidationState.Required);
        report.Findings.Should().BeEmpty();
        report.Summary.Should().Contain("Validate connectivity, credentials, account permissions");
    }

    [Theory]
    [InlineData("alpaca", "alpaca", "Alpaca")]
    [InlineData("  ALPACA  ", "alpaca", "Alpaca")]
    [InlineData("robinhood", "robinhood", "Robinhood")]
    [InlineData("ib", "ib", "Interactive Brokers")]
    [InlineData("interactivebrokers", "interactivebrokers", "Interactive Brokers")]
    [InlineData("tradier", "tradier", "tradier")]
    public void Evaluate_NormalizesGatewayIdAndResolvesDisplayName(
        string configuredGateway, string expectedGatewayId, string expectedDisplayName)
    {
        var configuration = CreateLiveConfiguration(configuredGateway);

        var report = BrokerageValidationEvaluator.Evaluate(configuration);

        report.GatewayId.Should().Be(expectedGatewayId);
        report.GatewayDisplayName.Should().Be(expectedDisplayName);
    }

    private static BrokerageConfiguration CreateLiveConfiguration(string gateway) => new()
    {
        Gateway = gateway,
        LiveExecutionEnabled = true
    };
}
