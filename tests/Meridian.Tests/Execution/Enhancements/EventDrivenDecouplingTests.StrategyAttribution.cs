using System.Text.Json;
using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Execution.Enhancements;

public sealed partial class EventDrivenDecouplingTests
{
    [Fact]
    public async Task LedgerPostingConsumer_StampsStrategyAndRunLineageOnFillJournals()
    {
        var evt = MakeBuyEvent("AAPL") with { Commission = 2m, StrategyId = " momentum-1 ", RunId = "run-42" };

        var writes = await CaptureCanonicalWritesAsync(evt);

        writes.Should().OnlyContain(write => write.Entry.Metadata.StrategyId == "momentum-1");
        writes.Should().OnlyContain(write =>
            write.Entry.Metadata.Tags![TradeFillCashFlowReplayCompatibility.StrategyRunIdTag] == "run-42");
    }

    [Fact]
    public async Task LedgerPostingConsumer_WithoutStrategyLineage_LeavesJournalMetadataUnchanged()
    {
        var writes = await CaptureCanonicalWritesAsync(MakeBuyEvent("AAPL"));

        writes.Should().OnlyContain(write => write.Entry.Metadata.StrategyId == null);
        writes.Should().OnlyContain(write =>
            !write.Entry.Metadata.Tags!.ContainsKey(TradeFillCashFlowReplayCompatibility.StrategyRunIdTag));
    }

    [Fact]
    public async Task StrategyAttributionUpgrade_RedeliveredFillMatchesJournalRetainedBeforeAttribution()
    {
        // The fill was posted before strategy attribution existed; the same fill redelivered
        // afterwards now carries lineage and must still resolve to the retained journal.
        var legacyEvent = MakeBuyEvent("AAPL") with { Commission = 2m };
        var retained = (await CaptureCanonicalWritesAsync(legacyEvent)).Select(ToRetainedFillRecord).ToList();
        var originalBytes = JsonSerializer.Serialize(retained);
        var journalStore = ReplayJournalStore(retained);
        using var governed = new DurableLedgerPostingTarget(journalStore.Object);
        var consumer = new LedgerPostingConsumer(
            new GovernedTradeFillLedgerPostingTarget(governed, journalStore.Object),
            NullLogger<LedgerPostingConsumer>.Instance,
            new InMemoryTradeFillPostingStore(TestPostingScope),
            TestPostingContext,
            securityValidationGate: new PassingSecurityValidationGate());

        consumer.Publish(legacyEvent with { StrategyId = "momentum-1", RunId = "run-42" });
        await consumer.DisposeAsync();

        JsonSerializer.Serialize(retained).Should().Be(originalBytes, "replay must never rewrite retained history");
        journalStore.Verify(store => store.AppendAsync(It.IsAny<LedgerJournalEntryWrite>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
