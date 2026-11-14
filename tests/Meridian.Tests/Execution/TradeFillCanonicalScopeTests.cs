using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Execution;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Execution;

public sealed class TradeFillCanonicalScopeTests
{
    [Fact]
    public void Canonical_cutover_uses_a_new_partition_while_legacy_identity_is_unchanged()
    {
        var options = new TradeFillLedgerPostingHostOptions
        {
            Enabled = true,
            AggregateId = Guid.NewGuid(),
            LedgerBookId = Guid.NewGuid(),
            PeriodId = Guid.NewGuid(),
            ExpectedPeriodVersion = 0
        };
        var legacy = options.BuildContext();
        var legacyIdentity = TradeFillPostingScopeIdentity.FromContext(legacy);
        legacyIdentity.AccountingProtocolVersion.Should().Be(0);
        options.CanonicalAccounting = new();
        var canonical = options.BuildContext();
        canonical.PostingScope.Should().Be(legacy.PostingScope + "/v2");
        canonical.AccountingProtocolVersion.Should().Be(2);
        TradeFillPostingScopeIdentity.FromContext(canonical).Should().NotBe(legacyIdentity);
        Action invalid = () => (canonical with { PostingScope = legacy.PostingScope }).Validate();
        invalid.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Production_store_aliases_share_one_authoritative_atomic_and_grant_boundary()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLedgerJournalStore("Host=127.0.0.1;Database=never-opened;Username=synthetic");
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ILedgerJournalStore>().Should().BeSameAs(provider.GetRequiredService<IAtomicTaxLotJournalStore>());
        provider.GetRequiredService<ILedgerJournalStore>().Should().BeSameAs(provider.GetRequiredService<IAutomatedPostingGrantStore>());
    }

    [Fact]
    public async Task Durable_fee_replay_compares_content_and_freezes_the_accepted_fee_list()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-fee-wal", Guid.NewGuid().ToString("N"));
        var context = new TradeFillLedgerPostingContext("fee-wal/v2", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            ExpectedPeriodVersion: 0, AccountingProtocolVersion: 2);
        var fees = new List<TradeExecutionFee> { new("fee-1", "Exchange", .25m, "USD") };
        var fill = new TradeExecutedEvent(Guid.NewGuid(), "order", "AAPL", OrderSide.Buy, 1, 100, 0, 0, 0,
            DateTimeOffset.Parse("2026-05-01T12:00:00Z"), "synthetic")
        { SourceMode = "Synthetic", Fees = fees };
        try
        {
            await using (var store = new WalTradeFillPostingStore(new(root, context), NullLogger<WalTradeFillPostingStore>.Instance))
            {
                await store.AcceptAsync(fill);
                fees[0] = fees[0] with { Amount = 99m };
                (await store.LoadPendingAsync()).Single().TradeEvent.Fees!.Single().Amount.Should().Be(.25m);
            }
            await using var restarted = new WalTradeFillPostingStore(new(root, context), NullLogger<WalTradeFillPostingStore>.Instance);
            var original = fill with { Fees = [new("fee-1", "Exchange", .25m, "USD")] };
            (await restarted.AcceptAsync(original)).ShouldEnqueue.Should().BeFalse();
            var conflict = () => restarted.AcceptAsync(original with { Fees = [new("fee-1", "Exchange", 1m, "USD")] });
            await conflict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different economic content*");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Independent_handoff_fallback_replays_identical_fee_content_after_restart()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-fee-fallback", Guid.NewGuid().ToString("N"));
        var context = new TradeFillLedgerPostingContext("fee-fallback/v2", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            ExpectedPeriodVersion: 0, AccountingProtocolVersion: 2);
        var fill = new TradeExecutedEvent(Guid.NewGuid(), "order", "AAPL", OrderSide.Buy, 1, 100, 0, 0, 0,
            DateTimeOffset.Parse("2026-05-01T12:00:00Z"), "synthetic")
        { SourceMode = "Synthetic", Fees = [new("fee-1", "Exchange", .25m, "USD")] };
        try
        {
            await using (var first = new AtomicTradeFillHandoffFailureStore(new(root, context)))
                await first.RetainAsync(fill, "synthetic primary WAL outage");
            await using var restarted = new AtomicTradeFillHandoffFailureStore(new(root, context));
            await restarted.RetainAsync(fill with { Fees = fill.Fees!.ToArray() }, "retry same retained source");
            var retained = (await restarted.LoadPendingAsync()).Single();
            retained.FailureCount.Should().Be(2);
            retained.TradeEvent.Fees!.Single().Amount.Should().Be(.25m);
            var conflict = () => restarted.RetainAsync(fill with { Fees = [new("fee-1", "Exchange", .50m, "USD")] }, "changed source");
            await conflict.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different accounting economics*");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Remembered_posted_fill_id_cannot_acknowledge_a_changed_or_unproven_v2_fill()
    {
        var context = new TradeFillLedgerPostingContext("posted-fill/v2", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            ExpectedPeriodVersion: 0, AccountingProtocolVersion: 2);
        var store = Substitute.For<ITradeFillPostingStore>();
        store.PostingScope.Returns(context.PostingScope);
        store.ScopeIdentity.Returns(TradeFillPostingScopeIdentity.FromContext(context));
        store.LoadPendingAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<PendingTradeFillPosting>());
        store.AcceptAsync(Arg.Any<TradeExecutedEvent>(), Arg.Any<CancellationToken>())
            .Returns(new TradeFillPostingAcceptance(null, false, true));
        var accounting = Substitute.For<IAutomatedTradeFillAccountingService>();
        accounting.TryRecoverCompletedAsync(Arg.Any<TradeExecutedEvent>(), context, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Meridian.Ledger.JournalEntry>?>(null));
        await using var consumer = LedgerPostingConsumer.CreateWithAccountingAdapter(Substitute.For<ITradeFillLedgerPostingTarget>(),
            NullLogger<LedgerPostingConsumer>.Instance, store, context, requireSecurityMasterPostingGate: false,
            requireCurrentAccountingEvidence: false, automatedAccounting: accounting);
        var fill = new TradeExecutedEvent(Guid.NewGuid(), "order", "AAPL", OrderSide.Buy, 1, 100, 0, 0, 0,
            DateTimeOffset.Parse("2026-05-01T12:00:00Z"), "synthetic")
        { SourceMode = "Synthetic" };
        var duplicate = () => consumer.PublishAsync(fill);
        await duplicate.Should().ThrowAsync<InvalidDataException>().WithMessage("*no complete, matching authoritative accounting receipts*");
    }

}
