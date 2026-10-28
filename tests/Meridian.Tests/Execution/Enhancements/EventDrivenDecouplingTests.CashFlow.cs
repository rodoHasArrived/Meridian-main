using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Meridian.Tests.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution.Enhancements;

public sealed partial class EventDrivenDecouplingTests
{
    [Theory]
    [InlineData(-10)]
    [InlineData(10)]
    public async Task LedgerPostingConsumer_DisposalReportsOnlyActualInvestingProceeds_AndReverses(int realizedPnl)
    {
        var ledger = new Meridian.Ledger.Ledger();
        ledger.PostLines(CanonicalCashFlowTestData.Start, "opening holding",
            [(LedgerAccounts.Securities("AAPL"), 100m - realizedPnl, 0m),
             (LedgerAccounts.RetainedEarnings, 0m, 100m - realizedPnl)]);
        var consumer = new LedgerPostingConsumer(new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance, new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext, projectionLedger: ledger, securityValidationGate: new PassingSecurityValidationGate());

        consumer.Publish(new TradeExecutedEvent(Guid.NewGuid(), "cash-flow-sale", "AAPL", OrderSide.Sell,
            1m, 100m, 0m, realizedPnl, 100m, CanonicalCashFlowTestData.Start.AddDays(1)));
        await consumer.DisposeAsync();

        ledger.Journal.Should().HaveCount(2);
        CanonicalCashFlowTestData.AssertReportAndReversal(ledger, ledger.Journal[1], 100m);
    }
}
