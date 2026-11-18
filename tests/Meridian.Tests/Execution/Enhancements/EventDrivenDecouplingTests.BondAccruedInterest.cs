using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;

namespace Meridian.Tests.Execution.Enhancements;

public sealed partial class EventDrivenDecouplingTests
{
    [Fact]
    public async Task CurrentEvidence_BondRoundTrip_PostsDirtySettlementThroughAccruedInterestReceivable()
    {
        // 100,000 face bought at 101.25 with 1.50 accrued, sold at 101.25 with 2.00 accrued.
        var buy = BondEvidenceEvent(OrderSide.Buy,
            new(Opening: new(SecurityTradePostingKind.OpenLong, 100_000m, 101_250m), FinancialAccountId: "broker")
            {
                AccruedInterest = 1_500m
            });
        var sell = BondEvidenceEvent(OrderSide.Sell,
            new(Closing: new(SecurityTradePostingKind.CloseLong, 100_000m, 101_250m, 101_250m), FinancialAccountId: "broker")
            {
                AccruedInterest = 2_000m,
                AccruedInterestRelieved = 1_500m
            });
        var target = new InMemoryTradeFillLedgerPostingTarget();

        var ledger = await DrainCurrentEvidenceAsync(target,
            new InMemoryTradeFillPostingStore(TestPostingScope), buy, sell);

        ledger.GetBalance(LedgerAccounts.CashAccount("broker")).Should().Be(500m,
            "clean legs net to zero; the seller received $500 more accrued than the buyer paid");
        ledger.GetBalance(LedgerAccounts.AccruedInterestReceivable("BOND1", "broker")).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.CouponIncomeFor("broker")).Should().Be(500m);
        ledger.GetBalance(LedgerAccounts.Securities("BOND1", "broker")).Should().Be(0m);
        target.RetainedEntries.Should().HaveCount(4).And.OnlyContain(entry => entry.IsBalanced);
    }

    [Fact]
    public async Task CurrentEvidence_AccruedInterestOnNonFaceValueFill_RemainsPending()
    {
        var evt = BondEvidenceEvent(OrderSide.Buy,
            new(Opening: new(SecurityTradePostingKind.OpenLong, 100_000m, 101_250m), FinancialAccountId: "broker")
            {
                AccruedInterest = 1_500m
            }) with { UsesFaceValuePercentageOfPar = false, FillPrice = 1.0125m };
        var store = new InMemoryTradeFillPostingStore(TestPostingScope);
        var target = new InMemoryTradeFillLedgerPostingTarget();

        await DrainCurrentEvidenceAsync(target, store, evt);

        target.RetainedEntries.Should().BeEmpty();
        (await store.LoadPendingAsync()).Should().ContainSingle();
    }

    private static TradeExecutedEvent BondEvidenceEvent(OrderSide side, TradeFillAccountingEvidence evidence)
        => new(Guid.NewGuid(), $"bond-{side}", "BOND1", side, 100_000m, 101.25m,
            0m, evidence.RealizedPnl, 0m, new DateTimeOffset(2026, 11, 1, 14, 30, 0, TimeSpan.Zero), "broker",
            UsesFaceValuePercentageOfPar: true)
        {
            AccountingEvidenceVersion = 1,
            AccountingEvidence = evidence
        };
}
