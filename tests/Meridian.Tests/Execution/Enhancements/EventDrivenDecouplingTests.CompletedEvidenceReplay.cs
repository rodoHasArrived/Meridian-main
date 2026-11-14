using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution.Enhancements;

public sealed partial class EventDrivenDecouplingTests
{
    [Fact]
    public async Task CompletedVersionOneFill_AfterCompactionAndRestart_RejectsChangedBasisWithoutChangingJournal()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-completed-fill-replay", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var options = new TradeFillPostingStoreOptions(root, TestPostingContext) { CompactionRecordThreshold = 1 };
        var target = new InMemoryTradeFillLedgerPostingTarget();
        var fill = new TradeExecutedEvent(Guid.NewGuid(), "completed-long-close", "AAPL", OrderSide.Sell,
            1m, 300m, 0m, 100m, 10_100m, DateTimeOffset.UtcNow, "broker-a")
        {
            AccountingEvidenceVersion = 1,
            AccountingEvidence = new TradeFillAccountingEvidence(
                Closing: new SecurityTradePosting(SecurityTradePostingKind.CloseLong, 1m, 300m, 200m),
                FinancialAccountId: "broker-a")
        };
        try
        {
            await using (var original = new WalTradeFillPostingStore(options, NullLogger<WalTradeFillPostingStore>.Instance))
            {
                var consumer = new LedgerPostingConsumer(target, NullLogger<LedgerPostingConsumer>.Instance,
                    original, TestPostingContext, requireCurrentAccountingEvidence: true,
                    securityValidationGate: new PassingSecurityValidationGate());
                await consumer.PublishAsync(fill);
                await consumer.DisposeAsync();
                (await original.LoadPendingAsync()).Should().BeEmpty();
            }

            var originalJournal = target.RetainedEntries.Should().ContainSingle().Subject;
            var changed = fill with
            {
                RealizedPnl = 90m,
                AccountingEvidence = fill.AccountingEvidence! with
                {
                    Closing = fill.AccountingEvidence.Closing! with { CarryingValue = 210m }
                }
            };
            await using var restarted = new WalTradeFillPostingStore(options, NullLogger<WalTradeFillPostingStore>.Instance);
            var retriedConsumer = new LedgerPostingConsumer(target, NullLogger<LedgerPostingConsumer>.Instance,
                restarted, TestPostingContext, requireCurrentAccountingEvidence: true,
                securityValidationGate: new PassingSecurityValidationGate());
            await retriedConsumer.PublishAsync(changed);
            await retriedConsumer.DisposeAsync();

            target.RetainedEntries.Should().ContainSingle().Which.Should().BeSameAs(originalJournal);
            var pending = (await restarted.LoadPendingAsync()).Should().ContainSingle().Subject;
            pending.TradeEvent.Should().Be(changed);
            pending.FailureCount.Should().BeGreaterThan(0);
            pending.LastFailure.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
