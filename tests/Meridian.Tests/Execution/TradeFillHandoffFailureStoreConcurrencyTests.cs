using System.Text.Json;
using FluentAssertions;
using Meridian.Execution.Events;
using Meridian.Execution.Serialization;
using Meridian.Execution.Sdk;

namespace Meridian.Tests.Execution;

/// <summary>
/// Guards the concurrent-writer and shutdown recovery scenarios for the durable last-resort
/// trade-fill accounting handoff shared by multiple host instances or processes.
/// </summary>
public sealed class TradeFillHandoffFailureStoreConcurrencyTests
{
    private const string PostingScope = "ledger-book/fund-alpha/period-open";
    private static readonly DateTimeOffset SessionOpen =
        new(2026, 7, 15, 13, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Scenario_ConcurrentStoreInstancesRetainDistinctBrokerFills_NoFailureIsLost()
    {
        var root = CreateTempRoot();
        var options = new TradeFillHandoffFailureStoreOptions(root, PostingScope);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await using var firstStore = new AtomicTradeFillHandoffFailureStore(options);
            await using var secondStore = new AtomicTradeFillHandoffFailureStore(options);

            // Prime both instances with the same empty disk view. Without reload-under-lock,
            // their subsequent independent snapshots deterministically overwrite one another.
            await Task.WhenAll(
                firstStore.LoadPendingAsync(timeout.Token),
                secondStore.LoadPendingAsync(timeout.Token));

            var appleFill = BuildFill(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "paper-aapl-1",
                "AAPL",
                211.42m,
                SessionOpen);
            var microsoftFill = BuildFill(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "paper-msft-1",
                "MSFT",
                506.18m,
                SessionOpen.AddMilliseconds(25));

            await Task.WhenAll(
                firstStore.RetainAsync(appleFill, "primary WAL unavailable", timeout.Token),
                secondStore.RetainAsync(microsoftFill, "primary snapshot unavailable", timeout.Token));

            await using var restartedStore = new AtomicTradeFillHandoffFailureStore(options);
            var recovered = await restartedStore.LoadPendingAsync(timeout.Token);

            recovered.Select(static item => item.TradeEvent.FillId)
                .Should().BeEquivalentTo(new[] { appleFill.FillId, microsoftFill.FillId });
            firstStore.ScopeIdentity.IsExact.Should().BeFalse(
                "legacy label-only construction must remain compatible for existing stores");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task Scenario_ReplayAcknowledgementRacesWithNewRetain_AcknowledgedFillDoesNotResurrect()
    {
        var root = CreateTempRoot();
        var options = new TradeFillHandoffFailureStoreOptions(root, PostingScope);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await using var replayStore = new AtomicTradeFillHandoffFailureStore(options);
            await using var intakeStore = new AtomicTradeFillHandoffFailureStore(options);
            var replayedFill = BuildFill(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "paper-aapl-replay",
                "AAPL",
                211.56m,
                SessionOpen.AddSeconds(1));
            var newlyFailedFill = BuildFill(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                "paper-msft-new",
                "MSFT",
                506.04m,
                SessionOpen.AddSeconds(1).AddMilliseconds(20));

            await replayStore.RetainAsync(replayedFill, "posting dependency unavailable", timeout.Token);
            await Task.WhenAll(
                replayStore.LoadPendingAsync(timeout.Token),
                intakeStore.LoadPendingAsync(timeout.Token));

            await Task.WhenAll(
                replayStore.MarkReplayedAsync(replayedFill.FillId, timeout.Token),
                intakeStore.RetainAsync(newlyFailedFill, "posting dependency still unavailable", timeout.Token));

            await using var restartedStore = new AtomicTradeFillHandoffFailureStore(options);
            var recovered = await restartedStore.LoadPendingAsync(timeout.Token);

            recovered.Should().ContainSingle();
            recovered[0].TradeEvent.FillId.Should().Be(newlyFailedFill.FillId);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task LoadPendingAsync_SameLabelButDifferentLedgerIdentity_FailsClosed()
    {
        var root = CreateTempRoot();
        var originalContext = BuildContext(
            aggregateId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            periodId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ledgerBookId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var mismatchedContext = BuildContext(
            aggregateId: originalContext.AggregateId,
            periodId: originalContext.PeriodId,
            ledgerBookId: Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"));
        var originalOptions = new TradeFillHandoffFailureStoreOptions(root, originalContext);
        var mismatchOptions = new TradeFillHandoffFailureStoreOptions(root, mismatchedContext);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var fill = BuildFill(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                "paper-scope-1",
                "AAPL",
                211.63m,
                SessionOpen.AddSeconds(2));
            await using (var originalStore = new AtomicTradeFillHandoffFailureStore(originalOptions))
            {
                originalStore.ScopeIdentity.Should().Be(
                    TradeFillPostingScopeIdentity.FromContext(originalContext));
                await originalStore.RetainAsync(fill, "primary posting rejected", timeout.Token);
            }

            await using var mismatchedStore = new AtomicTradeFillHandoffFailureStore(mismatchOptions);
            Func<Task> load = async () => await mismatchedStore.LoadPendingAsync(timeout.Token);

            await load.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*does not match configured scope*");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadPendingAsync_SameLabelAndLedgerIdentityButDifferentPolicy_FailsClosed(
        bool changeVersion)
    {
        var root = CreateTempRoot();
        var originalContext = BuildContext(
            aggregateId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            periodId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ledgerBookId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var mismatchedContext = changeVersion
            ? originalContext with { AccountingPolicyVersion = "2" }
            : originalContext with { AccountingPolicyId = "execution-trade-fill-v2" };
        var originalOptions = new TradeFillHandoffFailureStoreOptions(root, originalContext);
        var mismatchOptions = new TradeFillHandoffFailureStoreOptions(root, mismatchedContext);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var fill = BuildFill(
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                "paper-policy-1",
                "AAPL",
                211.78m,
                SessionOpen.AddSeconds(4));
            await using (var originalStore = new AtomicTradeFillHandoffFailureStore(originalOptions))
            {
                await originalStore.RetainAsync(fill, "primary posting rejected", timeout.Token);
            }

            await using var mismatchedStore = new AtomicTradeFillHandoffFailureStore(mismatchOptions);
            Func<Task> load = async () => await mismatchedStore.LoadPendingAsync(timeout.Token);

            await load.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*does not match configured scope*");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task LoadPendingAsync_LegacyLabelOnlySnapshot_LoadsOnlyInLegacyMode()
    {
        var root = CreateTempRoot();
        var legacyOptions = new TradeFillHandoffFailureStoreOptions(root, PostingScope);
        var exactContext = BuildContext(
            aggregateId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            periodId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ledgerBookId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var retainedFill = BuildFill(
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            "paper-legacy-1",
            "AAPL",
            211.82m,
            SessionOpen.AddSeconds(5));
        var retainedAtUtc = DateTimeOffset.UtcNow;
        var legacySnapshot = new TradeFillHandoffFailureSnapshot(
            Version: 1,
            PostingScope: PostingScope,
            Pending:
            [
                new RetainedTradeFillHandoffFailure(
                    retainedFill,
                    retainedAtUtc,
                    FailureCount: 1,
                    LastFailure: "legacy primary failure",
                    LastAttemptAtUtc: retainedAtUtc)
            ]);
        try
        {
            Directory.CreateDirectory(legacyOptions.ScopeDirectory);
            await File.WriteAllTextAsync(
                legacyOptions.SnapshotPath,
                JsonSerializer.Serialize(
                    legacySnapshot,
                    ExecutionJsonContext.Default.TradeFillHandoffFailureSnapshot));

            await using (var legacyStore = new AtomicTradeFillHandoffFailureStore(legacyOptions))
            {
                var pending = await legacyStore.LoadPendingAsync();
                pending.Should().ContainSingle(item => item.TradeEvent == retainedFill);
                legacyStore.ScopeIdentity.Should().Be(new TradeFillPostingScopeIdentity(PostingScope));
            }

            await using var exactStore = new AtomicTradeFillHandoffFailureStore(
                new TradeFillHandoffFailureStoreOptions(root, exactContext));
            Func<Task> loadExact = async () => await exactStore.LoadPendingAsync();

            await loadExact.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*does not match configured scope*");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public async Task DisposeAsync_InFlightRetainWaitingForProcessLock_DrainsBeforeClosingStore()
    {
        var root = CreateTempRoot();
        var options = new TradeFillHandoffFailureStoreOptions(root, PostingScope);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Directory.CreateDirectory(options.ScopeDirectory);
        var externalLock = new FileStream(
            options.LockPath,
            new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None
            });
        var store = new AtomicTradeFillHandoffFailureStore(options);
        try
        {
            var fill = BuildFill(
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                "paper-shutdown-1",
                "AAPL",
                211.71m,
                SessionOpen.AddSeconds(3));
            var retain = store.RetainAsync(fill, "primary posting unavailable during shutdown", timeout.Token);

            var dispose = store.DisposeAsync().AsTask();
            dispose.IsCompleted.Should().BeFalse(
                "disposal must wait for an operation admitted before shutdown");

            externalLock.Dispose();
            await retain.WaitAsync(timeout.Token);
            await dispose.WaitAsync(timeout.Token);

            await using var restartedStore = new AtomicTradeFillHandoffFailureStore(options);
            var recovered = await restartedStore.LoadPendingAsync(timeout.Token);
            recovered.Should().ContainSingle(item => item.TradeEvent.FillId == fill.FillId);
        }
        finally
        {
            externalLock.Dispose();
            await store.DisposeAsync();
            DeleteTempRoot(root);
        }
    }

    private static TradeFillLedgerPostingContext BuildContext(
        Guid aggregateId,
        Guid periodId,
        Guid ledgerBookId)
        => new(
            PostingScope,
            aggregateId,
            periodId,
            ledgerBookId);

    private static TradeExecutedEvent BuildFill(
        Guid fillId,
        string orderId,
        string symbol,
        decimal fillPrice,
        DateTimeOffset occurredAt)
        => new(
            fillId,
            orderId,
            symbol,
            OrderSide.Buy,
            FilledQuantity: 100m,
            FillPrice: fillPrice,
            Commission: 0.35m,
            RealizedPnl: 0m,
            NewCash: 250_000m - (100m * fillPrice) - 0.35m,
            OccurredAt: occurredAt,
            FinancialAccountId: "fund-alpha");

    private static string CreateTempRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "meridian-handoff-concurrency-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
