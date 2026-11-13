using FluentAssertions;
using System.Text.Json;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution.Enhancements;

public sealed partial class EventDrivenDecouplingTests
{
    [Fact]
    public async Task CurrentEvidence_ShortCoverBooksLiabilityAndAllCharges_AndReplaysExactly()
    {
        var opening = CurrentEvidenceEvent(OrderSide.Sell, 10m, 100m,
            new(Opening: new(SecurityTradePostingKind.OpenShort, 10m, 1_000m), FinancialAccountId: "broker"));
        var cover = CurrentEvidenceEvent(OrderSide.Buy, 4m, 90m,
            new(Closing: new(SecurityTradePostingKind.CoverShort, 4m, 360m, 400m),
                Fees: 2m, SlippageCost: 3m, FinancialAccountId: "broker"), commission: 1m);
        var target = new InMemoryTradeFillLedgerPostingTarget();

        for (var replay = 0; replay < 2; replay++)
        {
            var store = new InMemoryTradeFillPostingStore(TestPostingScope);
            var ledger = await DrainCurrentEvidenceAsync(target, store, opening, cover);
            (await store.LoadPendingAsync()).Should().BeEmpty();
            ledger.GetBalance(LedgerAccounts.CashAccount("broker")).Should().Be(634m);
            ledger.GetBalance(LedgerAccounts.ShortSecuritiesPayable("AAPL", "broker")).Should().Be(600m);
            ledger.GetBalance(LedgerAccounts.RealizedGainFor("broker")).Should().Be(40m);
            ledger.GetBalance(LedgerAccounts.CommissionExpenseFor("broker")).Should().Be(6m);
            ledger.GetBalance(LedgerAccounts.Securities("AAPL", "broker")).Should().Be(0m);
        }
        target.RetainedEntries.Should().HaveCount(3);
        target.RetainedEntries.Should().OnlyContain(entry => entry.IsBalanced);
        target.RetainedEntries.Should().OnlyContain(entry =>
            entry.Metadata.Tags.ContainsKey("tradeFill.accountingEvidence"));
    }

    [Fact]
    public async Task CurrentEvidence_CrossesZeroInBothDirectionsWithoutLosingOpeningOrClosingLeg()
    {
        var fills = new[]
        {
            CurrentEvidenceEvent(OrderSide.Buy, 5m, 100m,
                new(Opening: new(SecurityTradePostingKind.OpenLong, 5m, 500m), FinancialAccountId: "broker")),
            CurrentEvidenceEvent(OrderSide.Sell, 8m, 110m,
                new(Closing: new(SecurityTradePostingKind.CloseLong, 5m, 550m, 500m),
                    Opening: new(SecurityTradePostingKind.OpenShort, 3m, 330m), FinancialAccountId: "broker")),
            CurrentEvidenceEvent(OrderSide.Buy, 5m, 90m,
                new(Closing: new(SecurityTradePostingKind.CoverShort, 3m, 270m, 330m),
                    Opening: new(SecurityTradePostingKind.OpenLong, 2m, 180m), FinancialAccountId: "broker"))
        };

        var ledger = await DrainCurrentEvidenceAsync(new InMemoryTradeFillLedgerPostingTarget(),
            new InMemoryTradeFillPostingStore(TestPostingScope), fills);

        ledger.Journal.Should().HaveCount(3);
        ledger.GetBalance(LedgerAccounts.CashAccount("broker")).Should().Be(-70m);
        ledger.GetBalance(LedgerAccounts.Securities("AAPL", "broker")).Should().Be(180m);
        ledger.GetBalance(LedgerAccounts.ShortSecuritiesPayable("AAPL", "broker")).Should().Be(0m);
        ledger.GetBalance(LedgerAccounts.RealizedGainFor("broker")).Should().Be(110m);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown-version")]
    [InlineData("wrong-account")]
    [InlineData("wrong-side")]
    [InlineData("wrong-quantity")]
    [InlineData("wrong-gross")]
    [InlineData("wrong-realized")]
    [InlineData("missing-basis")]
    [InlineData("negative-fee")]
    [InlineData("unbacked-collateral")]
    [InlineData("unknown-side")]
    public async Task InvalidCurrentEvidence_PostsNeitherTradeNorCommissionAndRemainsPending(string defect)
    {
        var evidence = new TradeFillAccountingEvidence(
            Opening: new(SecurityTradePostingKind.OpenLong, 1m, 100m), FinancialAccountId: "broker");
        var evt = CurrentEvidenceEvent(OrderSide.Buy, 1m, 100m, evidence, commission: 1m);
        evt = defect switch
        {
            "missing" => evt with { AccountingEvidence = null },
            "unknown-version" => evt with { AccountingEvidenceVersion = 2 },
            "wrong-account" => evt with { AccountingEvidence = evidence with { FinancialAccountId = "other-broker" } },
            "wrong-side" => evt with { Side = OrderSide.Sell },
            "wrong-quantity" => evt with { FilledQuantity = 2m },
            "wrong-gross" => evt with
            {
                AccountingEvidence = evidence with
                {
                    Opening = evidence.Opening! with { GrossValue = 99m }
                }
            },
            "wrong-realized" => evt with { RealizedPnl = 1m },
            "missing-basis" => evt with
            {
                AccountingEvidence = evidence with
                {
                    Opening = null,
                    Closing = new(SecurityTradePostingKind.CoverShort, 1m, 100m)
                }
            },
            "negative-fee" => evt with { AccountingEvidence = evidence with { Fees = -1m } },
            "unbacked-collateral" => evt with { AccountingEvidence = evidence with { ShortCollateralReleased = 1m } },
            "unknown-side" => evt with { Side = (OrderSide)999 },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        var target = new InMemoryTradeFillLedgerPostingTarget();
        var store = new InMemoryTradeFillPostingStore(TestPostingScope);

        var ledger = await DrainCurrentEvidenceAsync(target, store, evt);

        ledger.Journal.Should().BeEmpty();
        target.RetainedEntries.Should().BeEmpty();
        (await store.LoadPendingAsync()).Should().ContainSingle().Which.FailureCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CurrentEvidence_ChangedFactsWithIdenticalJournalEconomics_CannotReplayAcrossStores()
    {
        var evt = CurrentEvidenceEvent(OrderSide.Buy, 1m, 100m,
            new(Opening: new(SecurityTradePostingKind.OpenLong, 1m, 100m), Fees: 1m, FinancialAccountId: "broker"));
        var target = new InMemoryTradeFillLedgerPostingTarget();
        await DrainCurrentEvidenceAsync(target, new InMemoryTradeFillPostingStore(TestPostingScope), evt);
        var altered = evt with
        {
            AccountingEvidence = evt.AccountingEvidence! with { Fees = 0m, SlippageCost = 1m }
        };
        var retryStore = new InMemoryTradeFillPostingStore(TestPostingScope);

        var retry = await DrainCurrentEvidenceAsync(target, retryStore, altered);

        retry.Journal.Should().BeEmpty();
        target.RetainedEntries.Should().HaveCount(2);
        (await retryStore.LoadPendingAsync()).Should().ContainSingle().Which.FailureCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CurrentEvidence_CannotRebookRetainedLegacyShortUnderSameFillIdentity()
    {
        var current = CurrentEvidenceEvent(OrderSide.Sell, 1m, 100m,
            new(Opening: new(SecurityTradePostingKind.OpenShort, 1m, 100m), FinancialAccountId: "broker"));
        var legacy = current with { AccountingEvidenceVersion = 0, AccountingEvidence = null };
        var target = new InMemoryTradeFillLedgerPostingTarget();
        await DrainCurrentEvidenceAsync(target, new InMemoryTradeFillPostingStore(TestPostingScope), legacy);
        var original = target.RetainedEntries.Single();
        var store = new InMemoryTradeFillPostingStore(TestPostingScope);

        var ledger = await DrainCurrentEvidenceAsync(target, store, current);

        ledger.Journal.Should().BeEmpty();
        target.RetainedEntries.Should().ContainSingle().Which.Should().BeSameAs(original);
        (await store.LoadPendingAsync()).Should().ContainSingle().Which.FailureCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CurrentEvidence_AcknowledgedFillRetainsReconstructibleSourceFactsInJournal()
    {
        var evidence = new TradeFillAccountingEvidence(
            Closing: new(SecurityTradePostingKind.CoverShort, 1m, 90m, 100.00000000000000000000000001m),
            Opening: new(SecurityTradePostingKind.OpenLong, 2m, 180m, BorrowedAmount: 50m),
            Fees: 1m, SlippageCost: 2m, ShortCollateralReleased: 30m, FinancialAccountId: "broker");
        var evt = CurrentEvidenceEvent(OrderSide.Buy, 3m, 90m, evidence);
        var target = new InMemoryTradeFillLedgerPostingTarget();
        var store = new InMemoryTradeFillPostingStore(TestPostingScope);

        await DrainCurrentEvidenceAsync(target, store, evt);

        (await store.LoadPendingAsync()).Should().BeEmpty();
        var json = target.RetainedEntries.First().Metadata.Tags["tradeFill.accountingEvidence.v1"];
        var retained = JsonSerializer.Deserialize(json, ExecutionJsonContext.Default.TradeFillAccountingEvidence)!;
        retained.Closing.Should().Be(evidence.Closing);
        retained.Opening.Should().Be(evidence.Opening);
        retained.Fees.Should().Be(1m);
        retained.SlippageCost.Should().Be(2m);
        retained.ShortCollateralReleased.Should().Be(30m);
        retained.FinancialAccountId.Should().Be("BROKER");
    }

    [Fact]
    public async Task CurrentEvidence_NumericScaleDifferencesKeepSameCanonicalSourceAndReplay()
    {
        var evt = CurrentEvidenceEvent(OrderSide.Buy, 1m, 100m,
            new(Opening: new(SecurityTradePostingKind.OpenLong, 1.00m, 100.00m),
                Fees: 1.00m, FinancialAccountId: "broker"));
        var target = new InMemoryTradeFillLedgerPostingTarget();
        await DrainCurrentEvidenceAsync(target, new InMemoryTradeFillPostingStore(TestPostingScope), evt);
        var retry = evt with
        {
            AccountingEvidence = evt.AccountingEvidence! with
            {
                Opening = new(SecurityTradePostingKind.OpenLong, 1m, 100m),
                Fees = 1m
            }
        };
        var store = new InMemoryTradeFillPostingStore(TestPostingScope);

        await DrainCurrentEvidenceAsync(target, store, retry);

        (await store.LoadPendingAsync()).Should().BeEmpty();
        target.RetainedEntries.Should().HaveCount(2);
    }

    [Fact]
    public async Task CurrentEvidence_FinancedTradeAndSevereLossRetainInvestingAndFinancingCashFlows()
    {
        var open = CurrentEvidenceEvent(OrderSide.Buy, 10m, 100m,
            new(Opening: new(SecurityTradePostingKind.OpenLong, 10m, 1_000m, BorrowedAmount: 500m),
                FinancialAccountId: "broker"));
        var close = CurrentEvidenceEvent(OrderSide.Sell, 10m, 20m,
            new(Closing: new(SecurityTradePostingKind.CloseLong, 10m, 200m, 1_000m, LoanRepaid: 500m),
                FinancialAccountId: "broker")) with
        { OccurredAt = open.OccurredAt.AddDays(1) };
        var store = new InMemoryTradeFillPostingStore(TestPostingScope);

        var ledger = await DrainCurrentEvidenceAsync(new InMemoryTradeFillLedgerPostingTarget(), store, open, close);

        (await store.LoadPendingAsync()).Should().BeEmpty();
        ledger.Journal.Should().HaveCount(4);
        var openingFlows = LedgerFinancialStatementBuilder.BuildForPeriod(ledger,
            open.OccurredAt.AddTicks(-1), open.OccurredAt.AddTicks(1), financialAccountId: "broker").CashFlow!;
        openingFlows.InvestingCashFlow.Should().Be(-1_000m);
        openingFlows.FinancingCashFlow.Should().Be(500m);
        openingFlows.EndingCash.Should().Be(-500m);
        openingFlows.IsReconciled.Should().BeTrue();
        var closingFlows = LedgerFinancialStatementBuilder.BuildForPeriod(ledger,
            close.OccurredAt.AddTicks(-1), close.OccurredAt.AddTicks(1), financialAccountId: "broker").CashFlow!;
        closingFlows.InvestingCashFlow.Should().Be(200m);
        closingFlows.FinancingCashFlow.Should().Be(-500m);
        closingFlows.EndingCash.Should().Be(-800m);
        closingFlows.IsReconciled.Should().BeTrue();
        ledger.GetBalance(LedgerAccounts.RealizedLossFor("broker")).Should().Be(800m);
    }

    [Fact]
    public async Task CurrentEvidence_ShortCollateralRemainsASeparateReconciledCashMovement()
    {
        var evt = CurrentEvidenceEvent(OrderSide.Sell, 10m, 100m,
            new(Opening: new(SecurityTradePostingKind.OpenShort, 10m, 1_000m),
                ShortCollateralHeld: 500m, FinancialAccountId: "broker"));
        var store = new InMemoryTradeFillPostingStore(TestPostingScope);

        var ledger = await DrainCurrentEvidenceAsync(new InMemoryTradeFillLedgerPostingTarget(), store, evt);

        (await store.LoadPendingAsync()).Should().BeEmpty();
        ledger.Journal.Should().HaveCount(2);
        ledger.Journal.Should().ContainSingle(journal => journal.Metadata.ActivityType == "trade-short-collateral");
        var cf = LedgerFinancialStatementBuilder.BuildForPeriod(ledger,
            evt.OccurredAt.AddTicks(-1), evt.OccurredAt.AddTicks(1), financialAccountId: "broker").CashFlow!;
        cf.InvestingCashFlow.Should().Be(500m);
        cf.FinancingCashFlow.Should().Be(0m);
        cf.EndingCash.Should().Be(500m);
        cf.IsReconciled.Should().BeTrue();
    }

    [Theory]
    [InlineData(90)]
    [InlineData(100)]
    [InlineData(110)]
    public async Task CurrentEvidence_ShortCoverGainBreakevenAndLossReportActualInvestingCash(int coverPrice)
    {
        var opening = CurrentEvidenceEvent(OrderSide.Sell, 10m, 100m,
            new(Opening: new(SecurityTradePostingKind.OpenShort, 10m, 1_000m), FinancialAccountId: "broker"));
        var cover = CurrentEvidenceEvent(OrderSide.Buy, 10m, coverPrice,
            new(Closing: new(SecurityTradePostingKind.CoverShort, 10m, 10m * coverPrice, 1_000m),
                FinancialAccountId: "broker")) with
        { OccurredAt = opening.OccurredAt.AddDays(1) };
        var store = new InMemoryTradeFillPostingStore(TestPostingScope);

        var ledger = await DrainCurrentEvidenceAsync(new InMemoryTradeFillLedgerPostingTarget(), store, opening, cover);

        (await store.LoadPendingAsync()).Should().BeEmpty();
        var cf = LedgerFinancialStatementBuilder.BuildForPeriod(ledger,
            cover.OccurredAt.AddTicks(-1), cover.OccurredAt.AddTicks(1), financialAccountId: "broker").CashFlow!;
        cf.InvestingCashFlow.Should().Be(-10m * coverPrice);
        cf.FinancingCashFlow.Should().Be(0m);
        cf.OperatingCashFlow.Should().Be(0m);
        cf.EndingCash.Should().Be(1_000m - 10m * coverPrice);
        cf.IsReconciled.Should().BeTrue();
    }

    private static TradeExecutedEvent CurrentEvidenceEvent(
        OrderSide side, decimal quantity, decimal price, TradeFillAccountingEvidence evidence,
        decimal commission = 0m)
        => new(Guid.NewGuid(), "accounting-evidence-order", "AAPL", side, quantity, price,
            commission, evidence.RealizedPnl, 0m,
            new DateTimeOffset(2026, 11, 1, 14, 30, 0, TimeSpan.Zero), "broker")
        {
            AccountingEvidenceVersion = 1,
            AccountingEvidence = evidence
        };

    private static async Task<Meridian.Ledger.Ledger> DrainCurrentEvidenceAsync(
        InMemoryTradeFillLedgerPostingTarget target, InMemoryTradeFillPostingStore store,
        params TradeExecutedEvent[] events)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(target, NullLogger<LedgerPostingConsumer>.Instance,
            store, TestPostingContext, projectionLedger: ledger,
            securityValidationGate: new PassingSecurityValidationGate());
        foreach (var evt in events)
            await consumer.PublishAsync(evt);
        await consumer.DisposeAsync();
        return ledger;
    }
}
