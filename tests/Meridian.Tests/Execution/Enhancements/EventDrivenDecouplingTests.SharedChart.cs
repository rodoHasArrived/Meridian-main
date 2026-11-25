using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution.Enhancements;

public sealed partial class EventDrivenDecouplingTests
{
    [Theory]
    [InlineData(OrderSide.Buy, 0)]
    [InlineData(OrderSide.Sell, 200)]
    [InlineData(OrderSide.Sell, -200)]
    public async Task LedgerPostingConsumer_ScopedPartialFills_KeepChartBalancesAndReplayIdentity(
        OrderSide side,
        int totalRealizedPnl)
    {
        var chart = StandardChartOfAccounts.Instance;
        var target = new InMemoryTradeFillLedgerPostingTarget();
        var occurredAt = new DateTimeOffset(2026, 10, 1, 14, 30, 0, TimeSpan.Zero);
        var fills = new[]
        {
            new TradeExecutedEvent(Guid.NewGuid(), "shared-order", "AAPL", side,
                4m, 100m, 1m, totalRealizedPnl * 0.4m, 0m, occurredAt, "broker-a"),
            new TradeExecutedEvent(Guid.NewGuid(), "shared-order", "AAPL", side,
                6m, 100m, 1m, totalRealizedPnl * 0.6m, 0m, occurredAt.AddSeconds(1), "broker-a"),
            new TradeExecutedEvent(Guid.NewGuid(), "other-account-order", "AAPL", OrderSide.Buy,
                2m, 100m, 0m, 0m, 0m, occurredAt.AddSeconds(2), "broker-b")
        };

        // A fresh handoff store models replay after its acknowledgement checkpoint was lost.
        // The authoritative journal target must recognize the same fill journals and lines.
        for (var replay = 0; replay < 2; replay++)
        {
            var ledger = new Meridian.Ledger.Ledger();
            var store = new InMemoryTradeFillPostingStore(TestPostingScope);
            var consumer = new LedgerPostingConsumer(target,
                NullLogger<LedgerPostingConsumer>.Instance, store, TestPostingContext,
                projectionLedger: ledger,
                securityValidationGate: new PassingSecurityValidationGate());
            foreach (var fill in fills)
                await consumer.PublishAsync(fill);
            await consumer.DisposeAsync();

            store.PostedCount.Should().Be(3);
            (await store.LoadPendingAsync()).Should().BeEmpty();
            ledger.Journal.Should().HaveCount(5);
            var securitiesBalance = side == OrderSide.Buy ? 1_000m : -(1_000m - totalRealizedPnl);
            var cashBalance = side == OrderSide.Buy ? -1_002m : 998m;
            ledger.GetBalance(chart.Resolve(LedgerAccountRole.Securities, "broker-a", "AAPL"))
                .Should().Be(securitiesBalance);
            ledger.GetBalance(chart.Resolve(LedgerAccountRole.Cash, "broker-a")).Should().Be(cashBalance);
            ledger.GetBalance(chart.Resolve(LedgerAccountRole.CommissionExpense, "broker-a")).Should().Be(2m);
            ledger.GetBalance(chart.Resolve(LedgerAccountRole.Securities, "broker-b", "AAPL"))
                .Should().Be(200m);
            ledger.GetBalance(chart.Resolve(LedgerAccountRole.Cash, "broker-b")).Should().Be(-200m);
            if (totalRealizedPnl > 0)
                ledger.GetBalance(chart.Resolve(LedgerAccountRole.RealizedGain, "broker-a"))
                    .Should().Be(totalRealizedPnl);
            if (totalRealizedPnl < 0)
                ledger.GetBalance(chart.Resolve(LedgerAccountRole.RealizedLoss, "broker-a"))
                    .Should().Be(-totalRealizedPnl);
        }

        target.RetainedEntries.Should().HaveCount(5,
            "distinct partial fills must book once each even when their order id is shared");
        target.RetainedEntries.SelectMany(static journal => journal.Lines)
            .Should().OnlyContain(line => line.Account.FinancialAccountId == "broker-a"
                || line.Account.FinancialAccountId == "broker-b");
    }
}
