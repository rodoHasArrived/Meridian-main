using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution.Enhancements;

public sealed partial class EventDrivenDecouplingTests
{
    [Theory]
    [InlineData(8, 200)]   // short 100 @ 10, cover @ 8  -> 200 gain
    [InlineData(12, -200)] // short 100 @ 10, cover @ 12 -> 200 loss
    public async Task LedgerPostingConsumer_BuyToCover_BooksRealizedPnlAndLeavesSecuritiesFlat(
        int coverPrice, int realizedPnl)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            projectionLedger: ledger,
            securityValidationGate: new PassingSecurityValidationGate());
        var openedAt = DateTimeOffset.UtcNow;

        // Open the short: Dr Cash 1,000 / Cr Securities 1,000.
        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-short", "AAPL", OrderSide.Sell,
            FilledQuantity: 100, FillPrice: 10m, Commission: 0m,
            RealizedPnl: 0m, NewCash: 0m, openedAt));
        // Cover it; the fill closes the position, so it carries the realized P&L.
        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-cover", "AAPL", OrderSide.Buy,
            FilledQuantity: 100, FillPrice: coverPrice, Commission: 0m,
            RealizedPnl: realizedPnl, NewCash: 0m, openedAt.AddSeconds(1)));

        await consumer.DisposeAsync();

        ledger.Journal.Should().HaveCount(2);
        ledger.GetBalance(LedgerAccounts.Securities("AAPL")).Should().Be(0m,
            "a flat position must leave no residual Securities balance");
        ledger.GetBalance(LedgerAccounts.Cash).Should().Be(realizedPnl,
            "net cash across the round trip equals the realized P&L");

        var cover = ledger.Journal[1];
        cover.Lines.Sum(line => line.Debit).Should().Be(cover.Lines.Sum(line => line.Credit));
        cover.Lines.Should().Contain(line => line.Account == LedgerAccounts.Cash && line.Credit == coverPrice * 100m);
        if (realizedPnl > 0)
            cover.Lines.Should().Contain(line => line.Account == LedgerAccounts.RealizedGain && line.Credit == realizedPnl);
        else
            cover.Lines.Should().Contain(line => line.Account == LedgerAccounts.RealizedLoss && line.Debit == -realizedPnl);
    }
}
