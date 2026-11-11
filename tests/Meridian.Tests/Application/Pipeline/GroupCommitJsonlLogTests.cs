using System.Collections.Concurrent;
using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Testing;
using Xunit;

namespace Meridian.Tests.Application.Pipeline;

public sealed class GroupCommitJsonlLogTests
{
    [Fact]
    public async Task AppendReservedAsync_ConcurrentCallers_WriteEveryLineExactlyOnce()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(AppendReservedAsync_ConcurrentCallers_WriteEveryLineExactlyOnce));
        var path = Path.Combine(artifacts.RootPath, "log.jsonl");
        await using var log = new GroupCommitJsonlLog(path, 10_000, retainFailedWrites: false, (_, _, _) => { });
        var expected = Enumerable.Range(0, 500).Select(i => $"{{\"n\":{i}}}").ToArray();

        var outcomes = await Task.WhenAll(expected.Select(line => Task.Run(async () =>
        {
            log.TryReserve().Should().BeTrue();
            return await log.AppendReservedAsync(line, CancellationToken.None);
        })));

        outcomes.Should().OnlyContain(outcome => outcome == JsonlLogAppendOutcome.Written);
        (await File.ReadAllLinesAsync(path)).Should().BeEquivalentTo(expected);
        log.WrittenCount.Should().Be(expected.Length);
        log.PendingCount.Should().Be(0);
        log.LostCount.Should().Be(0);
    }

    [Fact]
    public async Task AppendReservedAsync_WaitingCallers_ShareOneWrite()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var batches = new ConcurrentQueue<string[]>();
        await using var log = new GroupCommitJsonlLog(
            "unused.jsonl", 100, retainFailedWrites: false, (_, _, _) => { },
            async (_, lines, _) =>
            {
                batches.Enqueue([.. lines]);
                if (batches.Count == 1)
                {
                    firstWriteStarted.SetResult();
                    await release.Task;
                }
            });

        log.TryReserve().Should().BeTrue();
        var first = log.AppendReservedAsync("first", CancellationToken.None).AsTask();
        await firstWriteStarted.Task;
        var waiting = Enumerable.Range(0, 20).Select(i =>
        {
            log.TryReserve().Should().BeTrue();
            return log.AppendReservedAsync($"waiting-{i}", CancellationToken.None).AsTask();
        }).ToArray();
        release.SetResult();
        await first;
        await Task.WhenAll(waiting);

        // One write for the first caller and one for all twenty that queued behind it.
        batches.Select(batch => batch.Length).Should().Equal(1, 20);
    }

    [Fact]
    public async Task AppendReservedAsync_FailedWriteWithRetention_RetriesInArrivalOrder()
    {
        var failWrites = true;
        var written = new List<string>();
        var failures = new List<(int Count, bool Retained)>();
        await using var log = new GroupCommitJsonlLog(
            "unused.jsonl", 100, retainFailedWrites: true,
            (_, count, retained) => failures.Add((count, retained)),
            (_, lines, _) =>
            {
                if (failWrites)
                    throw new IOException("disk unavailable");
                written.AddRange(lines);
                return Task.CompletedTask;
            },
            retryBackoff: TimeSpan.Zero);

        log.TryReserve().Should().BeTrue();
        (await log.AppendReservedAsync("a", CancellationToken.None)).Should().Be(JsonlLogAppendOutcome.Retained);
        log.TryReserve().Should().BeTrue();
        (await log.AppendReservedAsync("b", CancellationToken.None)).Should().Be(JsonlLogAppendOutcome.Retained);
        log.PendingCount.Should().Be(2);
        failures.Should().Equal((1, true), (2, true));

        failWrites = false;
        log.TryReserve().Should().BeTrue();
        (await log.AppendReservedAsync("c", CancellationToken.None)).Should().Be(JsonlLogAppendOutcome.Written);

        written.Should().Equal("a", "b", "c");
        log.PendingCount.Should().Be(0);
        log.WrittenCount.Should().Be(3);
        log.LostCount.Should().Be(0);
    }

    [Fact]
    public async Task AppendReservedAsync_WhileBackingOff_HoldsNewLinesWithoutRetryingUntilFlushed()
    {
        var failWrites = true;
        var attempts = 0;
        var written = new List<string>();
        await using var log = new GroupCommitJsonlLog(
            "unused.jsonl", 100, retainFailedWrites: true, (_, _, _) => { },
            (_, lines, _) =>
            {
                attempts++;
                if (failWrites)
                    throw new IOException("disk unavailable");
                written.AddRange(lines);
                return Task.CompletedTask;
            },
            retryBackoff: TimeSpan.FromHours(1));

        log.TryReserve().Should().BeTrue();
        await log.AppendReservedAsync("a", CancellationToken.None);
        failWrites = false;
        log.TryReserve().Should().BeTrue();
        (await log.AppendReservedAsync("b", CancellationToken.None)).Should().Be(JsonlLogAppendOutcome.Retained);

        // An unwritable file is not re-attempted with the whole backlog on every record.
        attempts.Should().Be(1);
        log.PendingCount.Should().Be(2);

        (await log.FlushAsync(CancellationToken.None)).Should().Be(0);
        written.Should().Equal("a", "b");
    }

    [Fact]
    public async Task AppendReservedAsync_FailedWriteWithoutRetention_CountsTheLineAsLost()
    {
        await using var log = new GroupCommitJsonlLog(
            "unused.jsonl", 100, retainFailedWrites: false, (_, _, _) => { },
            (_, _, _) => throw new IOException("disk unavailable"));

        log.TryReserve().Should().BeTrue();
        (await log.AppendReservedAsync("a", CancellationToken.None)).Should().Be(JsonlLogAppendOutcome.Lost);

        log.PendingCount.Should().Be(0);
        log.LostCount.Should().Be(1);
    }

    [Fact]
    public async Task TryReserve_AtCapacity_RejectsAndCountsTheLineAsLost()
    {
        await using var log = new GroupCommitJsonlLog(
            "unused.jsonl", maxPendingLines: 2, retainFailedWrites: true, (_, _, _) => { },
            (_, _, _) => throw new IOException("disk unavailable"));

        log.TryReserve().Should().BeTrue();
        await log.AppendReservedAsync("a", CancellationToken.None);
        log.TryReserve().Should().BeTrue();
        await log.AppendReservedAsync("b", CancellationToken.None);

        log.TryReserve().Should().BeFalse();
        log.PendingCount.Should().Be(2);
        log.LostCount.Should().Be(1);
    }

    [Fact]
    public async Task TryReserveAfterRetryAsync_FullLogThatCanWriteAgain_DrainsAndReserves()
    {
        var failWrites = true;
        var written = new List<string>();
        await using var log = new GroupCommitJsonlLog(
            "unused.jsonl", maxPendingLines: 2, retainFailedWrites: true, (_, _, _) => { },
            (_, lines, _) =>
            {
                if (failWrites)
                    throw new IOException("disk unavailable");
                written.AddRange(lines);
                return Task.CompletedTask;
            },
            retryBackoff: TimeSpan.Zero);
        log.TryReserve().Should().BeTrue();
        await log.AppendReservedAsync("a", CancellationToken.None);
        log.TryReserve().Should().BeTrue();
        await log.AppendReservedAsync("b", CancellationToken.None);

        (await log.TryReserveAfterRetryAsync(CancellationToken.None)).Should().BeFalse("the file is still unwritable");
        log.LostCount.Should().Be(1);

        failWrites = false;
        (await log.TryReserveAfterRetryAsync(CancellationToken.None)).Should().BeTrue();
        (await log.AppendReservedAsync("c", CancellationToken.None)).Should().Be(JsonlLogAppendOutcome.Written);

        written.Should().Equal("a", "b", "c");
        log.PendingCount.Should().Be(0);
        log.LostCount.Should().Be(1);
    }

    [Fact]
    public async Task TryReserveAfterRetryAsync_FullLogStillBackingOff_DoesNotTouchTheFile()
    {
        var attempts = 0;
        await using var log = new GroupCommitJsonlLog(
            "unused.jsonl", maxPendingLines: 1, retainFailedWrites: true, (_, _, _) => { },
            (_, _, _) =>
            {
                attempts++;
                throw new IOException("disk unavailable");
            },
            retryBackoff: TimeSpan.FromHours(1));
        log.TryReserve().Should().BeTrue();
        await log.AppendReservedAsync("a", CancellationToken.None);

        (await log.TryReserveAfterRetryAsync(CancellationToken.None)).Should().BeFalse();

        attempts.Should().Be(1, "a full log must not hammer an unwritable file on every record");
        log.PendingCount.Should().Be(1);
        log.LostCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_RetriesRetainedLinesOnceMore_ThenCountsTheRestAsLost()
    {
        var attempts = 0;
        var log = new GroupCommitJsonlLog(
            "unused.jsonl", 100, retainFailedWrites: true, (_, _, _) => { },
            (_, _, _) =>
            {
                attempts++;
                throw new IOException("disk unavailable");
            });
        log.TryReserve().Should().BeTrue();
        await log.AppendReservedAsync("a", CancellationToken.None);

        await log.DisposeAsync();

        attempts.Should().Be(2);
        log.PendingCount.Should().Be(0);
        log.LostCount.Should().Be(1);
    }

    [Fact]
    public async Task AppendReservedAsync_CallerCancelled_StillWritesTheLine()
    {
        var written = new List<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        await using var log = new GroupCommitJsonlLog(
            "unused.jsonl", 100, retainFailedWrites: false, (_, _, _) => { },
            async (_, lines, _) =>
            {
                await release.Task;
                written.AddRange(lines);
            });
        log.TryReserve().Should().BeTrue();
        var append = log.AppendReservedAsync("a", cancellation.Token).AsTask();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => append);
        log.PendingCount.Should().Be(1, "a caller giving up does not withdraw its line");

        release.SetResult();
        (await log.FlushAsync(CancellationToken.None)).Should().Be(0);
        written.Should().Equal("a");
        log.LostCount.Should().Be(0);
    }

    [Fact]
    public async Task FlushAsync_CancelledDuringTheWrite_KeepsTheLinesForTheNextFlush()
    {
        var written = new List<string>();
        using var cancellation = new CancellationTokenSource();
        var attempt = 0;
        await using var log = new GroupCommitJsonlLog(
            "unused.jsonl", 100, retainFailedWrites: true, (_, _, _) => { },
            (_, lines, ct) =>
            {
                attempt++;
                if (attempt == 1)
                    throw new IOException("disk unavailable");
                if (attempt == 2)
                {
                    cancellation.Cancel();
                    ct.ThrowIfCancellationRequested();
                }

                written.AddRange(lines);
                return Task.CompletedTask;
            },
            retryBackoff: TimeSpan.FromHours(1));
        log.TryReserve().Should().BeTrue();
        (await log.AppendReservedAsync("a", CancellationToken.None)).Should().Be(JsonlLogAppendOutcome.Retained);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => log.FlushAsync(cancellation.Token));
        log.PendingCount.Should().Be(1);
        log.LostCount.Should().Be(0);

        (await log.FlushAsync(CancellationToken.None)).Should().Be(0);
        written.Should().Equal("a");
    }

    [Fact]
    public async Task AppendReservedAsync_LineAlreadyWritten_CompletesWhileALaterWriteIsInFlight()
    {
        // Under sustained load every caller used to take the write lock in turn, so a caller
        // whose line an earlier batch had already written stayed outstanding behind each later
        // write. Outstanding callers then grew without limit even though unwritten lines were
        // capped. Each caller must complete when its own batch is resolved.
        var gates = new[]
        {
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var started = new[]
        {
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var writes = 0;
        await using var log = new GroupCommitJsonlLog(
            "unused.jsonl", maxPendingLines: 3, retainFailedWrites: false, (_, _, _) => { },
            async (_, _, _) =>
            {
                var index = Interlocked.Increment(ref writes) - 1;
                started[index].SetResult();
                await gates[index].Task;
            });
        var timeout = TimeSpan.FromSeconds(10);

        Task<JsonlLogAppendOutcome> Append(string line)
        {
            log.TryReserve().Should().BeTrue();
            return log.AppendReservedAsync(line, CancellationToken.None).AsTask();
        }

        try
        {
            var first = Append("1");
            await started[0].Task.WaitAsync(timeout);      // write 1 holds line 1
            var second = Append("2");
            var third = Append("3");
            gates[0].SetResult();
            await started[1].Task.WaitAsync(timeout);      // write 2 holds lines 2 and 3
            var fourth = Append("4");
            gates[1].SetResult();
            await started[2].Task.WaitAsync(timeout);      // write 3 holds line 4 and is still blocked

            // Lines 1 to 3 are on disk, so their callers are done even though write 3 is in flight.
            (await first.WaitAsync(timeout)).Should().Be(JsonlLogAppendOutcome.Written);
            (await second.WaitAsync(timeout)).Should().Be(JsonlLogAppendOutcome.Written);
            (await third.WaitAsync(timeout)).Should().Be(JsonlLogAppendOutcome.Written);
            fourth.IsCompleted.Should().BeFalse();

            gates[2].SetResult();
            (await fourth.WaitAsync(timeout)).Should().Be(JsonlLogAppendOutcome.Written);
        }
        finally
        {
            // Never leave a write blocked: disposal waits for it, and a failed assertion above
            // must surface as a failure rather than a hung test run.
            foreach (var gate in gates)
                gate.TrySetResult();
        }
    }
}
