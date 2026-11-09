using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution.Enhancements;

/// <summary>
/// A variation-margin futures fill moves no notional, so the accounting handoff posts only the
/// cash it settled, against the futures settlement account, and never a securities purchase.
/// </summary>
public sealed partial class EventDrivenDecouplingTests
{
    [Fact]
    public async Task LedgerPostingConsumer_VariationMarginOpen_PostsOnlyItsCommission()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            projectionLedger: ledger,
            securityValidationGate: new PassingSecurityValidationGate());

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-es-open", "ESZ6", OrderSide.Buy,
            FilledQuantity: 1m, FillPrice: 6_000m, Commission: 2m, RealizedPnl: 0m, NewCash: 0m,
            DateTimeOffset.UtcNow, ContractMultiplier: 50m, SettlesVariationMargin: true));
        await consumer.DisposeAsync();

        ledger.Journal.Should().ContainSingle(entry => entry.Description.Contains("Commission"));
        ledger.Journal.SelectMany(static entry => entry.Lines)
            .Should().NotContain(line => line.Account == LedgerAccounts.Securities("ESZ6"));
    }

    [Fact]
    public async Task LedgerPostingConsumer_VariationMarginClose_PostsTheSettledCashFlow()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            new InMemoryTradeFillLedgerPostingTarget(),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            projectionLedger: ledger,
            securityValidationGate: new PassingSecurityValidationGate());

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-es-close", "ESZ6", OrderSide.Sell,
            FilledQuantity: 1m, FillPrice: 6_004m, Commission: 0m, RealizedPnl: -300m, NewCash: 0m,
            DateTimeOffset.UtcNow, ContractMultiplier: 50m, SettlesVariationMargin: true));
        await consumer.DisposeAsync();

        var entry = ledger.Journal.Should().ContainSingle().Subject;
        entry.Metadata.ActivityType.Should().Be("trade-variation-margin");
        entry.Lines.Single(line => line.Account == LedgerAccounts.FuturesMtmSettlement("ESZ6")).Debit.Should().Be(300m);
        entry.Lines.Single(line => line.Account == LedgerAccounts.Cash).Credit.Should().Be(300m);
    }
}
