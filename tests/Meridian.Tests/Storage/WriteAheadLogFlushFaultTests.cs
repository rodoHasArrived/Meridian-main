using FluentAssertions;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// A failed flush must cost one segment rotation, not the WAL: the fault is cleared by rebuilding
/// the active segment from the records not yet confirmed flushed, and nothing acknowledged to a
/// caller is lost across it.
/// </summary>
public sealed class WriteAheadLogFlushFaultTests : TempDirectoryAsyncTestBase
{
    private static WalOptions Batched(TimeSpan maxFlushDelay) => new()
    {
        SyncMode = WalSyncMode.BatchedSync,
        SyncBatchSize = 1_000,
        MaxFlushDelay = maxFlushDelay
    };

    [Fact]
    public async Task ForegroundFlushFailure_RebuildsSegmentAndKeepsEveryAcknowledgedRecord()
    {
        var sequences = new List<long>();
        await using (var wal = new WriteAheadLog(TestDataRoot, Batched(TimeSpan.FromMinutes(10))))
        {
            await wal.InitializeAsync();
            sequences.Add((await wal.AppendAsync("a", "trade")).Sequence);
            sequences.Add((await wal.AppendAsync("b", "trade")).Sequence);

            var failures = 1;
            wal.FlushFault = () =>
            {
                if (failures-- > 0)
                    throw new IOException("Injected: no space left on device");
            };

            var failedFlush = () => wal.FlushAsync();
            await failedFlush.Should().ThrowAsync<IOException>();

            // The next write-path call replaces the faulted segment instead of throwing forever.
            sequences.Add((await wal.AppendAsync("c", "trade")).Sequence);
            await wal.FlushAsync();
        }

        Directory.GetFiles(TestDataRoot, "*.wal").Should().HaveCount(2, "the faulted segment was replaced by a new one");

        await using var reopened = new WriteAheadLog(TestDataRoot, Batched(TimeSpan.FromMinutes(10)));
        await reopened.InitializeAsync();
        var recovered = new List<WalRecord>();
        await foreach (var record in reopened.GetUncommittedRecordsAsync())
            recovered.Add(record);

        recovered.Select(record => record.Sequence).Distinct().Should().BeEquivalentTo(sequences);
        reopened.CorruptedRecordCount.Should().Be(0);
    }

    [Fact]
    public async Task BackgroundFlushFailure_DoesNotBlockLaterWrites()
    {
        await using var wal = new WriteAheadLog(TestDataRoot, Batched(TimeSpan.FromMilliseconds(100)));
        await wal.InitializeAsync();

        // The fault must hit the delayed flush loop, not a flush this test triggers itself: an
        // append also flushes inline once MaxFlushDelay has elapsed since the last flush, which
        // on a slow machine can be true for the very first append.
        var faultObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var foreground = 0;
        var failures = 1;
        wal.FlushFault = () =>
        {
            if (Volatile.Read(ref foreground) == 0 && Interlocked.Decrement(ref failures) >= 0)
            {
                faultObserved.TrySetResult();
                throw new IOException("Injected: transient flush failure");
            }
        };

        // Each append that is not flushed inline leaves a record for the loop to pick up.
        long lastSequence = 0;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!faultObserved.Task.IsCompleted)
        {
            deadline.Token.ThrowIfCancellationRequested();
            Volatile.Write(ref foreground, 1);
            lastSequence = (await wal.AppendAsync("a", "trade")).Sequence;
            Volatile.Write(ref foreground, 0);
            await Task.WhenAny(faultObserved.Task, Task.Delay(TimeSpan.FromMilliseconds(500), deadline.Token));
        }

        // Before the fix this threw "new writes are blocked" until process restart.
        var after = await wal.AppendAsync("b", "trade");
        await wal.FlushAsync();
        await wal.CommitAsync(after.Sequence);

        var uncommitted = new List<WalRecord>();
        await foreach (var record in wal.GetUncommittedRecordsAsync())
            uncommitted.Add(record);
        uncommitted.Should().BeEmpty("every record was persisted and then committed");
        after.Sequence.Should().BeGreaterThan(lastSequence);
    }

    [Fact]
    public async Task AppendWhoseOwnFlushFails_IsNotCarriedIntoTheRebuiltSegment()
    {
        long kept;
        await using (var wal = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.EveryWrite }))
        {
            await wal.InitializeAsync();
            kept = (await wal.AppendAsync("kept", "trade")).Sequence;

            var failures = 1;
            wal.FlushFault = () =>
            {
                if (failures-- > 0)
                    throw new IOException("Injected");
            };

            // The caller is told this append failed, so it must not resurface after recovery.
            var failedAppend = () => wal.AppendAsync("rejected", "trade");
            await failedAppend.Should().ThrowAsync<IOException>();

            await wal.FlushAsync();
        }

        await using var reopened = new WriteAheadLog(TestDataRoot, new WalOptions { SyncMode = WalSyncMode.EveryWrite });
        await reopened.InitializeAsync();
        var payloads = new List<string>();
        await foreach (var record in reopened.GetUncommittedRecordsAsync())
            payloads.Add(record.Payload);

        payloads.Should().ContainSingle().Which.Should().Contain("kept");
        kept.Should().BeGreaterThan(0);
    }
}
