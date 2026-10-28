using System.Text.Json;
using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Execution.Enhancements;

public sealed partial class EventDrivenDecouplingTests
{
    [Theory]
    [InlineData(OrderSide.Buy, 0)]
    [InlineData(OrderSide.Sell, -10)]
    [InlineData(OrderSide.Sell, 10)]
    public async Task LegacyAttributedUpgrade_RealGovernedTargetReplaysPendingWalWithoutRewritingHistory(
        OrderSide side, int pnl)
    {
        var evt = MakeBuyEvent("AAPL") with { Side = side, RealizedPnl = pnl, Commission = 2m };
        var writes = await CaptureCanonicalWritesAsync(evt);
        var retained = writes.Select(write => ToRetainedFillRecord(RemoveNewCashFlowEvidence(write))).ToList();
        var originalBytes = JsonSerializer.Serialize(retained);
        var root = Path.Combine(Path.GetTempPath(), "meridian-legacy-fill-replay", Guid.NewGuid().ToString("N"));
        try
        {
            await using (var pending = new WalTradeFillPostingStore(new(root, TestPostingContext),
                             NullLogger<WalTradeFillPostingStore>.Instance))
                await pending.AcceptAsync(evt);
            var journalStore = ReplayJournalStore(retained);
            using var governed = new DurableLedgerPostingTarget(journalStore.Object);
            var target = new GovernedTradeFillLedgerPostingTarget(governed, journalStore.Object);
            await using var recovered = new WalTradeFillPostingStore(new(root, TestPostingContext),
                NullLogger<WalTradeFillPostingStore>.Instance);
            var ledger = new Meridian.Ledger.Ledger();
            var consumer = new LedgerPostingConsumer(target, NullLogger<LedgerPostingConsumer>.Instance,
                recovered, TestPostingContext, projectionLedger: ledger,
                securityValidationGate: new PassingSecurityValidationGate());

            await consumer.DisposeAsync();

            (await recovered.LoadPendingAsync()).Should().BeEmpty();
            ledger.Journal.Should().HaveCount(writes.Count);
            ledger.Journal.Should().OnlyContain(entry => !entry.Metadata.Tags!.Keys.Any(IsCashFlowEvidence));
            JsonSerializer.Serialize(retained).Should().Be(originalBytes, "replay must never invent historical attribution");
            JsonSerializer.Serialize(ledger.Journal).Should().Be(JsonSerializer.Serialize(retained.Select(record => record.Entry)));
            journalStore.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("money")]
    [InlineData("scope")]
    [InlineData("fingerprint")]
    [InlineData("unrelated-tag")]
    [InlineData("partial")]
    [InlineData("unknown")]
    [InlineData("v1")]
    [InlineData("category-only")]
    [InlineData("ambiguous")]
    [InlineData("line-id")]
    [InlineData("reordered")]
    public async Task LegacyUpgrade_RealGovernedTargetAndConsumerKeepChangedOrAttributedHistoryPending(string defect)
    {
        var evt = MakeBuyEvent("AAPL") with { Commission = 0m };
        var write = (await CaptureCanonicalWritesAsync(evt)).Single();
        var legacy = RemoveNewCashFlowEvidence(write);
        var retainedEntry = legacy.Entry;
        var tags = retainedEntry.Metadata.Tags!.ToDictionary(pair => pair.Key, pair => pair.Value);
        var id = retainedEntry.Lines[0].EntryId.ToString("D");
        switch (defect)
        {
            case "money":
                evt = evt with { FillPrice = evt.FillPrice + 1m };
                break;
            case "fingerprint":
                tags["tradeFill.fingerprint"] = "changed";
                break;
            case "unrelated-tag":
                tags["old-retained-evidence"] = "must-compare";
                break;
            case "partial":
                tags["cash-flow.group.v2." + id] = "Security trade";
                break;
            case "unknown":
                tags["CASH-FLOW.unknown.v9"] = "unrecognized";
                break;
            case "v1":
                foreach (var line in retainedEntry.Lines)
                    tags["cash-flow.group.v1." + line.EntryId.ToString("D")] = "old-reviewed-group";
                break;
            case "category-only":
                tags["cash-flow.category.v1." + id] = "Investing";
                break;
        }
        var lines = retainedEntry.Lines.ToArray();
        if (defect == "line-id")
        {
            var line = lines[0];
            lines[0] = new LedgerEntry(Guid.NewGuid(), line.JournalEntryId, line.Timestamp, line.Account,
                line.Debit, line.Credit, line.Description, line.Dimensions, line.Currency);
        }
        if (defect == "reordered")
            Array.Reverse(lines);
        retainedEntry = new JournalEntry(retainedEntry.JournalEntryId, retainedEntry.Timestamp,
            retainedEntry.Description, lines, retainedEntry.Metadata with { Tags = tags });
        var record = ToRetainedFillRecord(legacy with { Entry = retainedEntry });
        if (defect == "scope")
            record = record with { AggregateId = Guid.NewGuid() };
        var retained = new List<LedgerJournalEntryRecord> { record };
        if (defect == "ambiguous")
            retained.Add(record with { GlobalSequence = 2 });
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

    [Fact]
    public async Task NewFill_RealGovernedTargetRetainsNewAttributionAndProjectsExactNormalizedRecord()
    {
        var retained = new List<LedgerJournalEntryRecord>();
        var journalStore = ReplayJournalStore(retained, allowAppend: true);
        using var governed = new DurableLedgerPostingTarget(journalStore.Object);
        var pending = new InMemoryTradeFillPostingStore(TestPostingScope);
        var ledger = new Meridian.Ledger.Ledger();
        var consumer = new LedgerPostingConsumer(new GovernedTradeFillLedgerPostingTarget(governed, journalStore.Object),
            NullLogger<LedgerPostingConsumer>.Instance, pending, TestPostingContext, projectionLedger: ledger,
            securityValidationGate: new PassingSecurityValidationGate());

        consumer.Publish(MakeBuyEvent("AAPL") with { Commission = 0m });
        await consumer.DisposeAsync();

        (await pending.LoadPendingAsync()).Should().BeEmpty();
        retained.Should().ContainSingle();
        retained[0].Entry.Metadata.Tags!.Keys.Should().Contain(key => key.StartsWith("cash-flow.group.v2."));
        JsonSerializer.Serialize(ledger.Journal).Should().Be(JsonSerializer.Serialize(retained.Select(record => record.Entry)));
        journalStore.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task<IReadOnlyList<LedgerJournalEntryWrite>> CaptureCanonicalWritesAsync(TradeExecutedEvent evt)
    {
        var writes = new List<LedgerJournalEntryWrite>();
        var target = new InMemoryTradeFillLedgerPostingTarget { BeforePersistAsync = write => { writes.Add(write); return Task.CompletedTask; } };
        var consumer = new LedgerPostingConsumer(target, NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope), TestPostingContext,
            securityValidationGate: new PassingSecurityValidationGate());
        consumer.Publish(evt);
        await consumer.DisposeAsync();
        writes.Should().NotBeEmpty();
        return writes;
    }

    private static LedgerJournalEntryWrite RemoveNewCashFlowEvidence(LedgerJournalEntryWrite write)
        => write with
        {
            Entry = new JournalEntry(write.Entry.JournalEntryId, write.Entry.Timestamp, write.Entry.Description,
            write.Entry.Lines, write.Entry.Metadata with
            {
                Tags = write.Entry.Metadata.Tags!
                .Where(pair => !IsCashFlowEvidence(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value)
            })
        };

    private static bool IsCashFlowEvidence(string key) => key.StartsWith("cash-flow.", StringComparison.OrdinalIgnoreCase);

    private static LedgerJournalEntryRecord ToRetainedFillRecord(LedgerJournalEntryWrite write)
        => new(write.Entry, write.AggregateId, write.PeriodId, write.CommandId, write.CorrelationId, 1,
            write.Entry.Timestamp, write.AccountingBasis, write.AccountingPolicyId, write.AccountingPolicyVersion,
            write.RuleId, write.RuleVersion, write.SourceEventId, write.SourceJournalEntryId, write.PostingKind, write.AdjustmentApproval);

    private static Mock<ILedgerJournalStore> ReplayJournalStore(List<LedgerJournalEntryRecord> retained, bool allowAppend = false)
    {
        var store = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        store.As<ILedgerPostingIdentityCollisionLookup>().Setup(target => target.FindPostingIdentityCollisionsAsync(
                It.IsAny<LedgerPostingIdentity>(), It.IsAny<CancellationToken>()))
            .Returns((LedgerPostingIdentity identity, CancellationToken _) => Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(
                retained.Where(record => LedgerPostingIdentityCollisionLookupExtensions.IsCollision(record, identity)).ToArray()));
        store.Setup(target => target.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()))
            .Returns<LedgerJournalEntryWrite, CancellationToken>((write, _) =>
            {
                if (!allowAppend)
                    throw new InvalidOperationException("No append is permitted during retained replay.");
                retained.Add(ToRetainedFillRecord(write));
                return Task.CompletedTask;
            });
        return store;
    }
}
