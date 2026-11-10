using System.Text.Json;
using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Execution.Enhancements;

/// <summary>
/// A buy-to-cover fill posted before cover accounting retained a two-line trade journal. The same
/// fill now builds a three-line journal under the same journal id. A fill interrupted across the
/// upgrade must still finish replay against the journal it already retained, without that journal
/// being rewritten and without any other retained shape being accepted.
/// </summary>
public sealed partial class EventDrivenDecouplingTests
{
    private static TradeExecutedEvent MakeBuyToCoverEvent(decimal realizedPnl, decimal commission)
        => MakeBuyEvent("AAPL") with { RealizedPnl = realizedPnl, Commission = commission };

    [Theory]
    [InlineData(10)]
    [InlineData(-10)]
    public async Task PreCoverAccounting_EarlierShapeOfferedForABuyToCover_IsTheTwoLineGrossJournal(int pnl)
    {
        var evt = MakeBuyToCoverEvent(pnl, commission: 0m);

        var (canonical, earlier) = await CaptureTradeWriteAndEarlierShapeAsync(evt);

        canonical.Entry.Lines.Should().HaveCount(3);
        earlier.Should().NotBeNull();
        var lines = earlier!.Entry.Lines;
        earlier.Entry.JournalEntryId.Should().Be(canonical.Entry.JournalEntryId);
        lines.Should().HaveCount(2);
        // Every buy used to debit Securities and credit Cash at gross value.
        lines[0].Account.Should().Be(canonical.Entry.Lines[0].Account);
        lines[0].Debit.Should().Be(evt.GrossValue);
        lines[0].Credit.Should().Be(0m);
        lines[1].Debit.Should().Be(0m);
        lines[1].Credit.Should().Be(evt.GrossValue);
        lines[1].Account.Should().Be(canonical.Entry.Lines.Single(line => line.Credit == evt.GrossValue).Account);
        // Line ids are positional, so the earlier journal's two lines carry the first two ids.
        lines.Select(line => line.EntryId).Should().Equal(canonical.Entry.Lines.Take(2).Select(line => line.EntryId));
    }

    [Theory]
    [InlineData(OrderSide.Buy, 0)]
    [InlineData(OrderSide.Sell, 10)]
    [InlineData(OrderSide.Sell, -10)]
    public async Task PreCoverAccounting_NoEarlierShapeIsOfferedForFillsTheChangeDidNotAlter(OrderSide side, int pnl)
    {
        var evt = MakeBuyEvent("AAPL") with { Side = side, RealizedPnl = pnl };

        var (_, earlier) = await CaptureTradeWriteAndEarlierShapeAsync(evt);

        earlier.Should().BeNull();
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(-10, true)]
    [InlineData(10, false)]
    [InlineData(-10, false)]
    public async Task PreCoverAccounting_InterruptedBuyToCover_FinishesReplayAndKeepsTheRetainedJournal(
        int pnl, bool retainedCarriesCashFlowEvidence)
    {
        // The earlier version committed the trade journal and stopped before the commission.
        var evt = MakeBuyToCoverEvent(pnl, commission: 2m);
        var (_, earlier) = await CaptureTradeWriteAndEarlierShapeAsync(evt);
        var retainedTrade = ToRetainedFillRecord(
            retainedCarriesCashFlowEvidence ? earlier! : RemoveNewCashFlowEvidence(earlier!));
        var retained = new List<LedgerJournalEntryRecord> { retainedTrade };
        var originalTradeBytes = JsonSerializer.Serialize(retainedTrade);
        var journalStore = ReplayJournalStore(retained, allowAppend: true);
        using var governed = new DurableLedgerPostingTarget(journalStore.Object);
        var target = new GovernedTradeFillLedgerPostingTarget(governed, journalStore.Object);
        var pending = new InMemoryTradeFillPostingStore(TestPostingScope);
        await pending.AcceptAsync(evt);
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(target, NullLogger<LedgerPostingConsumer>.Instance,
            pending, TestPostingContext, projectionLedger: ledger,
            securityValidationGate: new PassingSecurityValidationGate());

        await consumer.DisposeAsync();

        (await pending.LoadPendingAsync()).Should().BeEmpty("the fill must no longer be stuck pending");
        JsonSerializer.Serialize(retained[0]).Should().Be(originalTradeBytes, "replay must not rewrite the retained journal");
        retained.Should().HaveCount(2, "only the commission journal was still owed");
        retained[1].Entry.Metadata.ActivityType.Should().Be("trade-commission");
        journalStore.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Once);
        ledger.Journal.Should().HaveCount(2);
        ledger.Journal[0].Lines.Should().HaveCount(2, "the projection shows the journal as it was retained");
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("account")]
    [InlineData("extra-line")]
    public async Task PreCoverAccounting_ARetainedJournalThatIsNotExactlyTheEarlierShape_StaysPending(string defect)
    {
        var evt = MakeBuyToCoverEvent(10m, commission: 0m);
        var (canonical, earlier) = await CaptureTradeWriteAndEarlierShapeAsync(evt);
        var entry = earlier!.Entry;
        var lines = entry.Lines.ToList();
        switch (defect)
        {
            case "amount":
                lines[0] = Relined(lines[0], lines[0].Account, lines[0].Debit + 1m, 0m);
                lines[1] = Relined(lines[1], lines[1].Account, 0m, lines[1].Credit + 1m);
                break;
            case "account":
                lines[0] = Relined(lines[0], canonical.Entry.Lines[2].Account, lines[0].Debit, 0m);
                break;
            case "extra-line":
                // Three lines, but not the three the fill now builds.
                lines[0] = Relined(lines[0], lines[0].Account, lines[0].Debit + 5m, 0m);
                lines.Add(Relined(canonical.Entry.Lines[2], canonical.Entry.Lines[2].Account, 0m, 5m));
                break;
        }
        var tampered = new JournalEntry(entry.JournalEntryId, entry.Timestamp, entry.Description, lines, entry.Metadata);
        var retained = new List<LedgerJournalEntryRecord> { ToRetainedFillRecord(earlier with { Entry = tampered }) };
        var originalBytes = JsonSerializer.Serialize(retained);
        var journalStore = ReplayJournalStore(retained);
        using var governed = new DurableLedgerPostingTarget(journalStore.Object);
        var target = new GovernedTradeFillLedgerPostingTarget(governed, journalStore.Object);
        var pending = new InMemoryTradeFillPostingStore(TestPostingScope);
        await pending.AcceptAsync(evt);
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(target, NullLogger<LedgerPostingConsumer>.Instance,
            pending, TestPostingContext, projectionLedger: ledger,
            securityValidationGate: new PassingSecurityValidationGate());

        await consumer.DisposeAsync();

        (await pending.LoadPendingAsync()).Should().ContainSingle().Which.FailureCount.Should().BeGreaterThan(0);
        ledger.Journal.Should().BeEmpty();
        JsonSerializer.Serialize(retained).Should().Be(originalBytes);
        journalStore.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(-10)]
    public async Task PreCoverAccounting_ANewBuyToCover_IsAppendedInTheCoverAccountingShape(int pnl)
    {
        var retained = new List<LedgerJournalEntryRecord>();
        var journalStore = ReplayJournalStore(retained, allowAppend: true);
        using var governed = new DurableLedgerPostingTarget(journalStore.Object);
        var pending = new InMemoryTradeFillPostingStore(TestPostingScope);
        var consumer = new LedgerPostingConsumer(new GovernedTradeFillLedgerPostingTarget(governed, journalStore.Object),
            NullLogger<LedgerPostingConsumer>.Instance, pending, TestPostingContext,
            securityValidationGate: new PassingSecurityValidationGate());

        consumer.Publish(MakeBuyToCoverEvent(pnl, commission: 0m));
        await consumer.DisposeAsync();

        (await pending.LoadPendingAsync()).Should().BeEmpty();
        retained.Should().ContainSingle().Which.Entry.Lines.Should().HaveCount(3,
            "the earlier shape only ever matches a journal that is already retained");
    }

    private static LedgerEntry Relined(LedgerEntry line, LedgerAccount account, decimal debit, decimal credit)
        => new(line.EntryId, line.JournalEntryId, line.Timestamp, account, debit, credit,
            line.Description, line.Dimensions, line.Currency);

    private static async Task<(LedgerJournalEntryWrite Canonical, LedgerJournalEntryWrite? Earlier)>
        CaptureTradeWriteAndEarlierShapeAsync(TradeExecutedEvent evt)
    {
        var target = new EarlierShapeCapturingTarget();
        var consumer = new LedgerPostingConsumer(target, NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope), TestPostingContext,
            securityValidationGate: new PassingSecurityValidationGate());
        consumer.Publish(evt);
        await consumer.DisposeAsync();
        return target.Offers.Single(offer => offer.Canonical.Entry.Metadata.ActivityType == "trade-fill");
    }

    /// <summary>Records what the consumer offers the posting target for each journal.</summary>
    private sealed class EarlierShapeCapturingTarget : ITradeFillLedgerPostingTarget
    {
        private readonly InMemoryTradeFillLedgerPostingTarget _inner = new();

        public List<(LedgerJournalEntryWrite Canonical, LedgerJournalEntryWrite? Earlier)> Offers { get; } = [];

        public Task<TradeFillLedgerPostingConfirmation> PostAndConfirmAsync(
            LedgerJournalEntryWrite write, CancellationToken ct = default)
            => PostAndConfirmAsync(write, retainedShapeAlternative: null, ct);

        public Task<TradeFillLedgerPostingConfirmation> PostAndConfirmAsync(
            LedgerJournalEntryWrite write, LedgerJournalEntryWrite? retainedShapeAlternative, CancellationToken ct = default)
        {
            Offers.Add((write, retainedShapeAlternative));
            return _inner.PostAndConfirmAsync(write, ct);
        }
    }
}
