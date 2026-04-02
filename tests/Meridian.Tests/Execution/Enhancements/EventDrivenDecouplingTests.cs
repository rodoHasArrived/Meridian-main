using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;

namespace Meridian.Tests.Execution.Enhancements;

/// <summary>
/// Tests for the Event-Driven Decoupling types (Phase 1).
/// Validates <see cref="TradeExecutedEvent"/> and <see cref="LedgerPostingConsumer"/>.
/// </summary>
public sealed class EventDrivenDecouplingTests
{
    // -------------------------------------------------------------------------
    // TradeExecutedEvent
    // -------------------------------------------------------------------------

    [Fact]
    public void TradeExecutedEvent_GrossValue_IsQuantityTimesPrice()
    {
        var evt = new TradeExecutedEvent(
            FillId: Guid.NewGuid(),
            OrderId: "ord-1",
            Symbol: "AAPL",
            Side: OrderSide.Buy,
            FilledQuantity: 100,
            FillPrice: 150m,
            Commission: 1m,
            RealizedPnl: 0m,
            NewCash: 85_000m,
            OccurredAt: DateTimeOffset.UtcNow);

        evt.GrossValue.Should().Be(100 * 150m);
    }

    [Fact]
    public void TradeExecutedEvent_FinancialAccountId_DefaultsToNull()
    {
        var evt = new TradeExecutedEvent(Guid.NewGuid(), "ord-1", "AAPL", OrderSide.Buy,
            100, 150m, 0m, 0m, 85_000m, DateTimeOffset.UtcNow);

        evt.FinancialAccountId.Should().BeNull();
    }

    // -------------------------------------------------------------------------
    // LedgerPostingConsumer — synchronous equivalence via async drain
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LedgerPostingConsumer_BuyEvent_PostsSecuritiesAndCashEntries()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger, NullLogger<LedgerPostingConsumer>.Instance);

        var evt = new TradeExecutedEvent(
            FillId: Guid.NewGuid(),
            OrderId: "ord-1",
            Symbol: "AAPL",
            Side: OrderSide.Buy,
            FilledQuantity: 100,
            FillPrice: 150m,
            Commission: 0m,
            RealizedPnl: 0m,
            NewCash: 85_000m,
            OccurredAt: DateTimeOffset.UtcNow);

        consumer.Publish(evt);
        await consumer.DisposeAsync(); // flushes and drains channel

        ledger.Journal.Should().ContainSingle();
        var entry = ledger.Journal[0];
        entry.Lines.Should().HaveCount(2);
        entry.Lines.Single(l => l.Account == LedgerAccounts.Securities("AAPL")).Debit.Should()
            .Be(100 * 150m);
        entry.Lines.Single(l => l.Account == LedgerAccounts.Cash).Credit.Should()
            .Be(100 * 150m);
    }

    [Fact]
    public async Task LedgerPostingConsumer_BuyWithCommission_PostsThreeEntries()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger, NullLogger<LedgerPostingConsumer>.Instance);

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-1", "AAPL", OrderSide.Buy,
            50, 200m, Commission: 5m, RealizedPnl: 0m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().HaveCount(2); // buy + commission
        ledger.Journal.Should().Contain(e => e.Description.Contains("Commission"));
    }

    [Fact]
    public async Task LedgerPostingConsumer_SellWithGain_PostsRealizedGainEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger, NullLogger<LedgerPostingConsumer>.Instance);

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-2", "AAPL", OrderSide.Sell,
            FilledQuantity: 100, FillPrice: 160m, Commission: 0m,
            RealizedPnl: 1_000m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().ContainSingle();
        var entry = ledger.Journal[0];
        entry.Lines.Should().Contain(l => l.Account == LedgerAccounts.RealizedGain && l.Credit == 1_000m);
    }

    [Fact]
    public async Task LedgerPostingConsumer_SellWithLoss_PostsRealizedLossEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(
            ledger, NullLogger<LedgerPostingConsumer>.Instance);

        consumer.Publish(new TradeExecutedEvent(
            Guid.NewGuid(), "ord-3", "MSFT", OrderSide.Sell,
            FilledQuantity: 100, FillPrice: 140m, Commission: 0m,
            RealizedPnl: -1_000m, NewCash: 0m, DateTimeOffset.UtcNow));

        await consumer.DisposeAsync();

        ledger.Journal.Should().ContainSingle();
        var entry = ledger.Journal[0];
        entry.Lines.Should().Contain(l => l.Account == LedgerAccounts.RealizedLoss && l.Debit == 1_000m);
    }

}
