using System.Text.Json;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Ledger;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Execution;

public sealed class TradeFillAccountingEvidencePersistenceTests
{
    private const string PostingScope = "evidence-tests/book/open-period";

    [Fact]
    public void LegacyEventJson_OmitsNewMembersAndDeserializesWithoutEvidence()
    {
        var legacy = Event() with { AccountingEvidenceVersion = 0, AccountingEvidence = null };

        var json = JsonSerializer.Serialize(legacy, ExecutionJsonContext.Default.TradeExecutedEvent);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("accountingEvidenceVersion", out _));
        Assert.False(document.RootElement.TryGetProperty("accountingEvidence", out _));
        var restored = JsonSerializer.Deserialize(json, ExecutionJsonContext.Default.TradeExecutedEvent);

        Assert.Equal(legacy, restored);
        Assert.Equal(0, restored!.AccountingEvidenceVersion);
        Assert.Null(restored.AccountingEvidence);
    }

    [Fact]
    public void VersionOneJson_RetainsExactEvidenceAndDistinguishesChangedBasis()
    {
        var fill = Event();

        var json = JsonSerializer.Serialize(fill, ExecutionJsonContext.Default.TradeExecutedEvent);
        var restored = JsonSerializer.Deserialize(json, ExecutionJsonContext.Default.TradeExecutedEvent);

        Assert.Equal(fill, restored);
        Assert.NotSame(fill.AccountingEvidence, restored!.AccountingEvidence);
        Assert.Equal(fill.AccountingEvidence!.Closing!.CarryingValue,
            restored.AccountingEvidence!.Closing!.CarryingValue);
        Assert.NotEqual(fill, ChangedBasis(fill));
        Assert.NotEqual(fill, fill with { AccountingEvidenceVersion = 0 });
    }

    [Fact]
    public async Task WalRestart_RetainsExactEvidenceAndRejectsSameFillWithChangedBasis()
    {
        var root = TempRoot();
        var options = new TradeFillPostingStoreOptions(root, PostingScope) { CompactionRecordThreshold = 1 };
        var fill = Event();
        try
        {
            await using (var original = new WalTradeFillPostingStore(options,
                             NullLogger<WalTradeFillPostingStore>.Instance))
            {
                var accepted = await original.AcceptAsync(fill);
                Assert.True(accepted.ShouldEnqueue);
            }

            await using var restarted = new WalTradeFillPostingStore(options,
                NullLogger<WalTradeFillPostingStore>.Instance);
            var retained = Assert.Single(await restarted.LoadPendingAsync());
            Assert.Equal(fill, retained.TradeEvent);
            var exactRetry = await restarted.AcceptAsync(fill with
            {
                AccountingEvidence = fill.AccountingEvidence! with
                {
                    Closing = fill.AccountingEvidence.Closing! with { }
                }
            });
            Assert.False(exactRetry.ShouldEnqueue);
            await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.AcceptAsync(ChangedBasis(fill)));
            Assert.Equal(fill, Assert.Single(await restarted.LoadPendingAsync()).TradeEvent);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CompletedWalRetry_RequiresExactEconomicsBeforeReturningAlreadyPosted()
    {
        var root = TempRoot();
        var fill = Event();
        try
        {
            await using var store = new WalTradeFillPostingStore(new TradeFillPostingStoreOptions(root, PostingScope),
                NullLogger<WalTradeFillPostingStore>.Instance);
            await store.AcceptAsync(fill);
            await store.MarkPostedAsync(fill.FillId);

            var exactRetry = await store.AcceptAsync(fill with { AccountingEvidence = fill.AccountingEvidence! with { } });
            Assert.True(exactRetry.WasAlreadyPosted);
            Assert.False(exactRetry.ShouldEnqueue);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AcceptAsync(ChangedBasis(fill)));
            Assert.Empty(await store.LoadPendingAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CompletedWalCache_EvictsEconomicsWithItsBoundedIdentityAndRequiresNewVerification()
    {
        var root = TempRoot();
        var fill = Event();
        try
        {
            var options = new TradeFillPostingStoreOptions(root, PostingScope) { MaxRememberedPostedFillIds = 1 };
            await using var store = new WalTradeFillPostingStore(options, NullLogger<WalTradeFillPostingStore>.Instance);
            await store.AcceptAsync(fill);
            await store.MarkPostedAsync(fill.FillId);
            var second = fill with { FillId = Guid.NewGuid() };
            await store.AcceptAsync(second);
            await store.MarkPostedAsync(second.FillId);

            var revalidation = await store.AcceptAsync(ChangedBasis(fill));

            Assert.True(revalidation.ShouldEnqueue);
            Assert.False(revalidation.WasAlreadyPosted);
            Assert.Equal(ChangedBasis(fill), Assert.Single(await store.LoadPendingAsync()).TradeEvent);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LatestAcceptedWalRecord_AfterEarlierCompletionAndCacheGrowth_RemainsPendingForVerification()
    {
        foreach (var changedEconomics in new[] { false, true })
        {
            var root = TempRoot();
            var options = new TradeFillPostingStoreOptions(root, PostingScope) { MaxRememberedPostedFillIds = 16 };
            var first = Event();
            var second = first with { FillId = Guid.NewGuid() };
            var acceptedRetry = changedEconomics ? ChangedBasis(first) : first;
            try
            {
                // This is the crash history of a writer whose completed cache held only one
                // identity: B evicted A, then A was durably accepted for fresh verification.
                // A larger restart cache must not reinterpret that last acceptance as an ack.
                await using (var wal = new WriteAheadLog(options.WalDirectory,
                                 new WalOptions { SyncMode = WalSyncMode.EveryWrite }))
                {
                    await wal.InitializeAsync();
                    foreach (var fill in new[] { first, second })
                    {
                        var pending = new TradeFillPendingWalPayload(PostingScope, fill, fill.OccurredAt);
                        await wal.AppendAsync(JsonSerializer.Serialize(pending,
                            ExecutionJsonContext.Default.TradeFillPendingWalPayload), "TradeFillPending");
                        var completed = new TradeFillStatusWalPayload(PostingScope, fill.FillId, fill.OccurredAt, null);
                        await wal.AppendAsync(JsonSerializer.Serialize(completed,
                            ExecutionJsonContext.Default.TradeFillStatusWalPayload), "TradeFillPosted");
                    }

                    var latest = new TradeFillPendingWalPayload(PostingScope, acceptedRetry, acceptedRetry.OccurredAt);
                    await wal.AppendAsync(JsonSerializer.Serialize(latest,
                        ExecutionJsonContext.Default.TradeFillPendingWalPayload), "TradeFillPending");
                }

                await using var restarted = new WalTradeFillPostingStore(options, NullLogger<WalTradeFillPostingStore>.Instance);
                var retained = Assert.Single(await restarted.LoadPendingAsync());
                Assert.Equal(acceptedRetry, retained.TradeEvent);
                var retry = await restarted.AcceptAsync(acceptedRetry);
                Assert.False(retry.WasAlreadyPosted);
                Assert.NotNull(retry.Posting);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FallbackRestart_RetainsExactEvidenceAndRejectsSameFillWithChangedBasis()
    {
        var root = TempRoot();
        var options = new TradeFillHandoffFailureStoreOptions(root, PostingScope);
        var fill = Event();
        try
        {
            await using (var original = new AtomicTradeFillHandoffFailureStore(options))
                await original.RetainAsync(fill, "Primary handoff unavailable.");

            await using var restarted = new AtomicTradeFillHandoffFailureStore(options);
            Assert.Equal(fill, Assert.Single(await restarted.LoadPendingAsync()).TradeEvent);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                restarted.RetainAsync(ChangedBasis(fill), "Conflicting retry."));
            Assert.Equal(fill, Assert.Single(await restarted.LoadPendingAsync()).TradeEvent);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static TradeExecutedEvent Event() => new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"), "basis-close", "AAPL", OrderSide.Sell,
        1m, 2m, 0.1m, 0.9999999999999999999999999999m, 10m,
        new DateTimeOffset(2026, 10, 1, 14, 30, 0, TimeSpan.Zero), "broker-a")
    {
        AccountingEvidenceVersion = 1,
        AccountingEvidence = new TradeFillAccountingEvidence(
            Closing: new SecurityTradePosting(SecurityTradePostingKind.CloseLong, 1m, 2m,
                1.0000000000000000000000000001m),
            Fees: 0.02m,
            SlippageCost: 0.03m,
            FinancialAccountId: "broker-a")
    };

    private static TradeExecutedEvent ChangedBasis(TradeExecutedEvent fill) => fill with
    {
        AccountingEvidence = fill.AccountingEvidence! with
        {
            Closing = fill.AccountingEvidence.Closing! with { CarryingValue = 1m }
        }
    };

    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-accounting-evidence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
