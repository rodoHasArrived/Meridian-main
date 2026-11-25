using FluentAssertions;
using Meridian.Backtesting.Portfolio;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Face-value (bond) lots in the native simulated portfolio: quantity is face and prices are a
/// clean percentage of par, so every money amount is face × price / 100.
/// </summary>
public sealed class SimulatedPortfolioFaceValueTests
{
    private const string Bond = "UST-4.25-2034";
    private static readonly DateTimeOffset T0 = new(2024, 1, 2, 21, 0, 0, TimeSpan.Zero);

    private static SimulatedPortfolio CreatePortfolio(BacktestLedger? ledger = null, ICommissionModel? commission = null)
        => new(
            [FinancialAccount.CreateDefaultBrokerage(1_000_000m, 0d, 0d)],
            BacktestDefaults.DefaultBrokerageAccountId,
            commission ?? new FixedCommissionModel(0m),
            ledger,
            T0.AddDays(-1),
            new Dictionary<string, (SecurityLotUnitKind UnitKind, decimal Multiplier)>
            {
                [Bond] = (SecurityLotUnitKind.FaceValue, 1m)
            })
        {
            FailOnLedgerIntegrityBreak = true
        };

    private static FillEvent Fill(long face, decimal pricePer100, DateTimeOffset at, decimal commission = 0m)
        => new(Guid.NewGuid(), Guid.NewGuid(), Bond, face, pricePer100, commission, at);

    [Fact]
    public void Buy_100kFaceAt101_25_Debits101250AndMarksToMarket()
    {
        var ledger = new BacktestLedger();
        var portfolio = CreatePortfolio(ledger);

        portfolio.ProcessFill(Fill(100_000, 101.25m, T0));

        portfolio.Cash.Should().Be(1_000_000m - 101_250m);
        var position = portfolio.GetCurrentPositions()[Bond];
        position.AverageCostBasis.Should().Be(101.25m, "average cost stays in the percent-of-par quote convention");
        position.OpenLots!.Should().ContainSingle().Which.CostBasis().Should().Be(101_250m);

        portfolio.UpdateLastPrice(Bond, 102m);
        var snapshot = portfolio.TakeSnapshot(T0.AddDays(1), DateOnly.FromDateTime(T0.AddDays(1).UtcDateTime));

        snapshot.LongMarketValue.Should().Be(102_000m);
        snapshot.TotalEquity.Should().Be(1_000_000m + 750m);
        snapshot.Positions[Bond].UnrealizedPnl.Should().Be(750m);
        portfolio.LedgerIntegrityBreaks.Should().BeEmpty("ledger securities and cash must agree with face-value economics");
    }

    [Fact]
    public void PartialSale_RealisesFaceScaledGainAndKeepsLedgerBalanced()
    {
        var ledger = new BacktestLedger();
        var portfolio = CreatePortfolio(ledger);
        portfolio.ProcessFill(Fill(100_000, 101.25m, T0));

        portfolio.ProcessFill(Fill(-40_000, 103m, T0.AddDays(1)));

        portfolio.Cash.Should().Be(1_000_000m - 101_250m + 41_200m);
        var closed = portfolio.GetClosedLots(Bond).Should().ContainSingle().Which;
        closed.Quantity.Should().Be(40_000m);
        closed.EntryPrice.Should().Be(101.25m);
        closed.ClosePrice.Should().Be(103m);
        closed.RealizedPnl.Should().Be(700m);
        portfolio.GetCurrentPositions()[Bond].RealizedPnl.Should().Be(700m);

        portfolio.UpdateLastPrice(Bond, 103m);
        portfolio.TakeSnapshot(T0.AddDays(1), DateOnly.FromDateTime(T0.AddDays(1).UtcDateTime))
            .LongMarketValue.Should().Be(61_800m);
        portfolio.LedgerIntegrityBreaks.Should().BeEmpty();
    }

    [Fact]
    public void PercentageCommission_ChargesOnDollarNotionalForFaceValueSymbols()
    {
        var conventions = new Dictionary<string, (SecurityLotUnitKind UnitKind, decimal Multiplier)>
        {
            [Bond] = (SecurityLotUnitKind.FaceValue, 1m)
        };
        var model = FaceValueQuotedCommissionModel.Wrap(new PercentageCommissionModel(basisPoints: 10m, minimumPerOrder: 0m), conventions);

        model.Calculate(Bond, 100_000, 101.25m).Should().Be(101.25m, "10 bps of $101,250");
        model.Calculate("SPY", 100, 400m).Should().Be(40m, "share symbols are unaffected");
    }

    [Theory]
    [InlineData(SecurityLotUnitKind.Contracts, 100)]
    [InlineData(SecurityLotUnitKind.FaceValue, 1000)]
    public void UnsupportedConventions_StillThrow(SecurityLotUnitKind unitKind, int multiplier)
    {
        var act = () => new SimulatedPortfolio(
            [FinancialAccount.CreateDefaultBrokerage(1_000m, 0d, 0d)],
            BacktestDefaults.DefaultBrokerageAccountId,
            new FixedCommissionModel(0m),
            lotConventions: new Dictionary<string, (SecurityLotUnitKind UnitKind, decimal Multiplier)>
            {
                ["X"] = (unitKind, multiplier)
            });

        act.Should().Throw<NotSupportedException>();
    }
}
