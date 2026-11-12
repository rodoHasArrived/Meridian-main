using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Margin;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Tests.Execution;

/// <summary>
/// Paper futures settle daily variation margin: opening moves no notional, each settlement pays
/// or receives the price change since the last one, and a close settles only the change since
/// the last settlement. One E-mini contract (multiplier 50) bought at 6,000 throughout.
/// </summary>
public sealed class PaperTradingPortfolioVariationMarginTests
{
    private const string Es = "ESZ6";
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Open_MovesNoNotional_AndValuesOnlyTheUnsettledChange()
    {
        var portfolio = new PaperTradingPortfolio(1_000_000m);

        portfolio.ApplyFill(Future(OrderSide.Buy, qty: 1m, price: 6_000m) with { Commission = 2m });
        portfolio.UpdateMarketPrice(Es, 6_010m);

        portfolio.Cash.Should().Be(999_998m, "only the commission moves cash on an open");
        portfolio.UnrealisedPnl.Should().Be(500m);
        portfolio.PortfolioValue.Should().Be(1_000_498m);
        var position = portfolio.Positions[Es];
        position.ContractMultiplier.Should().Be(50m);
        position.AverageCostBasis.Should().Be(6_000m, "an unsettled position is carried at its entry price");
        var account = portfolio.GetAccountSnapshot(PaperTradingPortfolio.DefaultAccountId)!;
        account.LongMarketValue.Should().Be(300_500m, "exposure still reports the notional");
    }

    [Fact]
    public void Settlements_AndClose_MoveTheWholeGainInSteps()
    {
        var portfolio = new PaperTradingPortfolio(1_000_000m);
        portfolio.ApplyFill(Future(OrderSide.Buy, qty: 1m, price: 6_000m));

        portfolio.ApplyVariationMarginSettlement(PaperTradingPortfolio.DefaultAccountId, Es, 6_010m, T0.AddHours(6))
            .Should().Be(500m);
        portfolio.Cash.Should().Be(1_000_500m);
        portfolio.UnrealisedPnl.Should().Be(0m, "settling resets the unrealised change at the settlement price");
        portfolio.Positions[Es].AverageCostBasis.Should().Be(6_010m, "the position is carried at its last settlement");

        portfolio.ApplyVariationMarginSettlement(PaperTradingPortfolio.DefaultAccountId, Es, 6_005m, T0.AddDays(1).AddHours(6))
            .Should().Be(-250m);

        portfolio.ApplyFill(Future(OrderSide.Sell, qty: 1m, price: 6_020m));

        portfolio.Cash.Should().Be(1_001_000m, "(6,020 - 6,000) x 50 over the round trip");
        portfolio.RealisedPnl.Should().Be(1_000m);
        portfolio.Positions.Should().NotContainKey(Es);
    }

    [Fact]
    public void Short_SettlesWithTheOppositeSign()
    {
        var portfolio = new PaperTradingPortfolio(1_000_000m);
        portfolio.ApplyFill(Future(OrderSide.Sell, qty: 2m, price: 6_000m));

        portfolio.Cash.Should().Be(1_000_000m, "a short open receives no proceeds either");
        portfolio.ApplyVariationMarginSettlement(PaperTradingPortfolio.DefaultAccountId, Es, 5_990m, T0.AddHours(6))
            .Should().Be(1_000m);
        portfolio.ApplyFill(Future(OrderSide.Buy, qty: 2m, price: 5_995m));

        portfolio.Cash.Should().Be(1_000_500m, "(6,000 - 5,995) x 2 x 50");
        portfolio.RealisedPnl.Should().Be(500m);
    }

    [Fact]
    public void Adding_CarriesTheQuantityWeightedPrice_AndPartialClosesSettleFromIt()
    {
        var portfolio = new PaperTradingPortfolio(1_000_000m);
        portfolio.ApplyFill(Future(OrderSide.Buy, qty: 1m, price: 6_000m));
        portfolio.ApplyFill(Future(OrderSide.Buy, qty: 1m, price: 6_010m));

        portfolio.GetOpenVariationMarginPositions().Single().LastSettlementPrice.Should().Be(6_005m);
        portfolio.UpdateMarketPrice(Es, 6_010m);
        portfolio.UnrealisedPnl.Should().Be(500m, "(6,010 - 6,000) x 50 on the first contract, 0 on the second");

        portfolio.ApplyFill(Future(OrderSide.Sell, qty: 1m, price: 6_020m));
        portfolio.Cash.Should().Be(1_000_750m, "(6,020 - 6,005) x 50 on the closed contract");
        portfolio.UpdateMarketPrice(Es, 6_020m);
        portfolio.UnrealisedPnl.Should().Be(750m);
        (portfolio.RealisedPnl + portfolio.UnrealisedPnl).Should().Be(1_500m,
            "the two contracts gained 20 and 10 points");
    }

    [Fact]
    public void CrossingThroughZero_ClosesTheLong_AndOpensTheShortAtTheFillPrice()
    {
        var portfolio = new PaperTradingPortfolio(1_000_000m);
        portfolio.ApplyFill(Future(OrderSide.Buy, qty: 1m, price: 6_000m));

        portfolio.ApplyFill(Future(OrderSide.Sell, qty: 3m, price: 6_010m));

        portfolio.Cash.Should().Be(1_000_500m);
        var open = portfolio.GetOpenVariationMarginPositions().Single();
        open.Quantity.Should().Be(-2m);
        open.LastSettlementPrice.Should().Be(6_010m);
    }

    [Fact]
    public void Settlement_IsANoOp_ForFlatAndNotionalModelPositions()
    {
        var portfolio = new PaperTradingPortfolio(1_000_000m);
        portfolio.ApplyRecordedFill(Future(OrderSide.Buy, qty: 1m, price: 6_000m));

        portfolio.ApplyVariationMarginSettlement(PaperTradingPortfolio.DefaultAccountId, Es, 6_010m, T0.AddHours(6))
            .Should().Be(0m, "a legacy record stays on the notional model");
        portfolio.ApplyVariationMarginSettlement(PaperTradingPortfolio.DefaultAccountId, "NQZ6", 21_000m, T0.AddHours(6))
            .Should().Be(0m);
        portfolio.GetOpenVariationMarginPositions().Should().BeEmpty();
    }

    [Fact]
    public void StampedRecord_ReplaysOnVariationMargin_AndLegacyContinuesUntilFlat()
    {
        var stamped = new PaperTradingPortfolio(1_000_000m);
        stamped.ApplyRecordedFill(Future(OrderSide.Buy, qty: 1m, price: 6_000m) with
        {
            BookedVariationMargin = true,
            BookedMoneyMultiplier = 50m
        });
        stamped.Cash.Should().Be(1_000_000m);

        var legacy = new PaperTradingPortfolio(1_000_000m);
        legacy.ApplyRecordedFill(Future(OrderSide.Buy, qty: 1m, price: 6_000m));
        legacy.GetOpenVariationMargin(Es).Should().BeFalse();
        legacy.GetOpenMoneyMultiplier(Es).Should().Be(1m);

        var unstampedLive = () => legacy.ApplyFill(Future(OrderSide.Buy, qty: 1m, price: 6_000m));
        unstampedLive.Should().Throw<InvalidOperationException>().WithMessage("*variation margin*");

        legacy.ApplyFill(Future(OrderSide.Sell, qty: 1m, price: 6_010m) with
        {
            BookedVariationMargin = false,
            BookedMoneyMultiplier = 1m
        });
        legacy.Cash.Should().Be(1_000_010m, "the legacy position closes on its own 1x model");

        legacy.ApplyFill(Future(OrderSide.Buy, qty: 1m, price: 6_010m));
        legacy.Cash.Should().Be(1_000_010m, "once flat, a new holding opens on variation margin");
        legacy.GetOpenVariationMargin(Es).Should().BeTrue();
    }

    [Fact]
    public void VariationMarginCashFlows_PostBalancedEntries_AgainstTheSettlementAccount()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(1_000_000m, ledger);
        var opening = ledger.JournalEntryCount; // the initial capital entry
        portfolio.ApplyFill(Future(OrderSide.Buy, qty: 1m, price: 6_000m));
        ledger.JournalEntryCount.Should().Be(opening, "an open moves no cash and posts nothing");

        portfolio.ApplyVariationMarginSettlement(PaperTradingPortfolio.DefaultAccountId, Es, 6_010m, T0.AddHours(6));
        portfolio.ApplyFill(Future(OrderSide.Sell, qty: 1m, price: 6_004m) with { Timestamp = T0.AddDays(1) });

        // The journal is ordered by timestamp, so select the variation-margin postings by account.
        var postings = ledger.Journal
            .Where(entry => entry.Lines.Any(line => line.Account == LedgerAccounts.FuturesMtmSettlement(Es)))
            .ToArray();
        postings.Should().HaveCount(2);
        postings.Should().AllSatisfy(entry =>
            entry.Lines.Sum(static line => line.Debit).Should().Be(entry.Lines.Sum(static line => line.Credit)));
        var settlement = postings[0];
        settlement.Lines.Single(line => line.Account == LedgerAccounts.Cash).Debit.Should().Be(500m);
        settlement.Lines.Single(line => line.Account == LedgerAccounts.FuturesMtmSettlement(Es)).Credit.Should().Be(500m);
        var close = postings[1];
        close.Lines.Single(line => line.Account == LedgerAccounts.Cash).Credit.Should().Be(300m, "(6,004 - 6,010) x 50");
        close.Lines.Should().NotContain(line => line.Account == LedgerAccounts.Securities(Es));
    }

    [Fact]
    public void RegTMarginCheck_AddsNoInitialRequirement_AndCountsTheUnsettledChange()
    {
        var portfolio = new PaperTradingPortfolio(
        [
            new AccountDefinition("margin-1", "Reg T Margin", AccountKind.Brokerage, 3_000m,
                MarginType: MarginAccountType.RegT),
        ]);
        portfolio.ApplyFill("margin-1", Future(OrderSide.Buy, qty: 1m, price: 6_000m));
        portfolio.UpdateMarketPrice("margin-1", Es, 5_990m);

        var status = portfolio.CheckMarginStatus("margin-1", new Dictionary<string, decimal> { [Es] = 5_990m });

        portfolio.Cash.Should().Be(3_000m);
        portfolio.MarginBalance.Should().Be(0m, "no notional is borrowed for a futures position");
        status.PortfolioRequirement.NotionalValue.Should().Be(0m);
        status.IsMarginCall.Should().BeFalse("equity is $3,000 less the $500 unsettled loss");
        portfolio.PortfolioValue.Should().Be(2_500m);
    }

    private static ExecutionReport Future(OrderSide side, decimal qty, decimal price) => new()
    {
        OrderId = Guid.NewGuid().ToString("N"),
        ReportType = ExecutionReportType.Fill,
        Symbol = Es,
        Side = side,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = qty,
        FilledQuantity = qty,
        FillPrice = price,
        Commission = 0m,
        Timestamp = T0,
        AssetClass = "Future",
        LotUnitKind = SecurityLotUnitKind.Contracts,
        LotMultiplier = 50m
    };
}
