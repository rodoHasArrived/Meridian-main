using FluentAssertions;
using Meridian.Execution.Adapters;
using Meridian.Execution.Interfaces;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Strategies.Live;
using Moq;
using Xunit;

namespace Meridian.Tests.Strategies;

/// <summary>
/// The live account snapshot a strategy reads must describe the book it actually holds: the
/// market values of its longs and shorts, its margin draw, and what the state proves about
/// short selling and margin, rather than fixed zeros and "not permitted".
/// </summary>
public sealed class LiveStrategyExecutionContextAccountTests
{
    [Fact]
    public void Accounts_ReportLongAndShortMarketValuesAndInferredPermissions()
    {
        var positions = new Dictionary<string, IPosition>(StringComparer.OrdinalIgnoreCase)
        {
            // Long 100 bought at 50, now worth 55: market value 5,500.
            ["AAPL"] = new ExecutionPosition("AAPL", 100, 50m, UnrealisedPnl: 500m, RealisedPnl: 0m),
            // Short 20 sold at 100, now quoted 90: market value -1,800 (a liability).
            ["TSLA"] = new ExecutionPosition("TSLA", -20, 100m, UnrealisedPnl: 200m, RealisedPnl: 0m),
        };
        var portfolio = new Mock<IPortfolioState>();
        portfolio.SetupGet(state => state.Cash).Returns(-1_000m);
        portfolio.SetupGet(state => state.BuyingPower).Returns(20_000m);
        portfolio.SetupGet(state => state.PortfolioValue).Returns(2_700m);
        portfolio.SetupGet(state => state.Positions).Returns(positions);
        var gateway = new Mock<IOrderGateway>();
        gateway.SetupGet(item => item.BrokerName).Returns("Paper");
        var context = new LiveStrategyExecutionContext(gateway.Object, new LiveMarketDataCache(),
            portfolio.Object, new HashSet<string> { "AAPL", "TSLA" });

        var account = context.Accounts.Values.Single();

        account.LongMarketValue.Should().Be(5_500m);
        account.ShortMarketValue.Should().Be(-1_800m);
        account.MarginBalance.Should().Be(-1_000m, "negative cash is a margin draw");
        account.Rules.AllowMargin.Should().BeTrue("buying power above cash means the portfolio is margin-aware");
        account.Rules.AllowShortSelling.Should().BeTrue("an account holding a short evidently permits shorting");
    }

    [Fact]
    public void Accounts_WithoutEvidence_StayConservative()
    {
        var portfolio = new Mock<IPortfolioState>();
        portfolio.SetupGet(state => state.Cash).Returns(10_000m);
        portfolio.SetupGet(state => state.BuyingPower).Returns(10_000m);
        portfolio.SetupGet(state => state.PortfolioValue).Returns(10_000m);
        portfolio.SetupGet(state => state.Positions).Returns(new Dictionary<string, IPosition>());
        var gateway = new Mock<IOrderGateway>();
        gateway.SetupGet(item => item.BrokerName).Returns("Paper");
        var context = new LiveStrategyExecutionContext(gateway.Object, new LiveMarketDataCache(),
            portfolio.Object, new HashSet<string>());

        var account = context.Accounts.Values.Single();

        account.LongMarketValue.Should().Be(0m);
        account.ShortMarketValue.Should().Be(0m);
        account.MarginBalance.Should().Be(0m);
        account.Rules.AllowMargin.Should().BeFalse();
        account.Rules.AllowShortSelling.Should().BeFalse();
    }

    [Fact]
    public void Accounts_FractionalShort_IsCountedAsShort()
    {
        // Whole-share Quantity rounds -0.5 to 0, which used to file the short's negative value
        // under long market value.
        var positions = new Dictionary<string, IPosition>(StringComparer.OrdinalIgnoreCase)
        {
            ["BRK.A"] = new ExecutionPosition("BRK.A", 0, 600_000m, UnrealisedPnl: 0m, RealisedPnl: 0m)
            {
                ExactQuantity = -0.5m
            },
        };
        var portfolio = new Mock<IPortfolioState>();
        portfolio.SetupGet(state => state.Cash).Returns(400_000m);
        portfolio.SetupGet(state => state.BuyingPower).Returns(400_000m);
        portfolio.SetupGet(state => state.PortfolioValue).Returns(100_000m);
        portfolio.SetupGet(state => state.Positions).Returns(positions);
        var gateway = new Mock<IOrderGateway>();
        gateway.SetupGet(item => item.BrokerName).Returns("Paper");
        var context = new LiveStrategyExecutionContext(gateway.Object, new LiveMarketDataCache(),
            portfolio.Object, new HashSet<string> { "BRK.A" });

        var account = context.Accounts.Values.Single();

        account.ShortMarketValue.Should().Be(-300_000m);
        account.LongMarketValue.Should().Be(0m);
        account.Rules.AllowShortSelling.Should().BeTrue();
    }
}
