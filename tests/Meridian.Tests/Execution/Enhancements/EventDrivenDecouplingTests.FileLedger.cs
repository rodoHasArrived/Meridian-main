using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution.Enhancements;

/// <summary>
/// Paper fills posted through <see cref="LedgerPostingConsumer"/> to the file-backed journal: the
/// books survive a restart, and a fill redelivered after the restart is confirmed, never doubled.
/// </summary>
public sealed partial class EventDrivenDecouplingTests
{
    [Fact]
    public async Task FileLedger_PaperFills_SurviveRestartAndRedeliveryDoesNotDoubleCount()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-file-fill-ledger-tests", Guid.NewGuid().ToString("N"));
        var directory = FileTradeFillLedgerJournalStore.DirectoryFor(root, TestPostingScope);
        var buy = MakeBuyEvent("AAPL") with { Commission = 1.25m };
        var sell = new TradeExecutedEvent(
            Guid.NewGuid(), "ord-sell", "AAPL", OrderSide.Sell,
            FilledQuantity: 4, FillPrice: 110m, Commission: 0m,
            RealizedPnl: 40m, NewCash: 0m, DateTimeOffset.UtcNow);
        try
        {
            var projection = new Meridian.Ledger.Ledger();
            await using (var store = new FileTradeFillLedgerJournalStore(directory, TestPostingScope, policy: null))
            {
                var consumer = new LedgerPostingConsumer(
                    FileTradeFillLedger.CreatePostingTarget(store),
                    NullLogger<LedgerPostingConsumer>.Instance,
                    new InMemoryTradeFillPostingStore(TestPostingScope),
                    TestPostingContext,
                    projectionLedger: projection,
                    securityValidationGate: new PassingSecurityValidationGate());
                consumer.Publish(buy);
                consumer.Publish(sell);
                await consumer.DisposeAsync();
                await consumer.ProcessingCompletion;
            }

            projection.Journal.Should().NotBeEmpty();

            await using var reopened = new FileTradeFillLedgerJournalStore(directory, TestPostingScope, policy: null);
            (await FileTradeFillLedger.VerifyBeforeHostStartupAsync(reopened)).IsIntact.Should().BeTrue();
            var retained = await reopened.GetAllAsync();
            retained.Select(record => record.Entry.JournalEntryId)
                .Should().Equal(projection.Journal.Select(entry => entry.JournalEntryId));
            retained.Should().OnlyContain(record =>
                record.AggregateId == TestPostingContext.AggregateId && record.PeriodId == TestPostingContext.PeriodId);
            retained.Select(record => record.Entry.Metadata.FillId).Should().Contain([buy.FillId, sell.FillId]);
            (await reopened.LoadLedgerAsync()).TrialBalance().Should().BeEquivalentTo(projection.TrialBalance());

            // A restarted process that lost its handoff state redelivers both fills.
            var redeliveryProjection = new Meridian.Ledger.Ledger();
            var redelivery = new LedgerPostingConsumer(
                FileTradeFillLedger.CreatePostingTarget(reopened),
                NullLogger<LedgerPostingConsumer>.Instance,
                new InMemoryTradeFillPostingStore(TestPostingScope),
                TestPostingContext,
                projectionLedger: redeliveryProjection,
                securityValidationGate: new PassingSecurityValidationGate());
            redelivery.Publish(buy);
            redelivery.Publish(sell);
            await redelivery.DisposeAsync();
            await redelivery.ProcessingCompletion;

            (await reopened.GetAllAsync()).Should().HaveCount(retained.Count, "a redelivered fill confirms its retained journal");
            redeliveryProjection.TrialBalance().Should().BeEquivalentTo(projection.TrialBalance());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
