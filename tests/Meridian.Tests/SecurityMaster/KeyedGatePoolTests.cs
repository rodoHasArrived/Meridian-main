using FluentAssertions;
using Meridian.Application.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The keyed gate pool must give the same mutual exclusion the static per-security semaphore
/// dictionaries gave — while reclaiming entries on last release, so a long-running host's memory
/// does not grow with every security ever amended or edited.
/// </summary>
public sealed class KeyedGatePoolTests
{
    [Fact]
    public async Task AcquireAndRelease_ReclaimsTheEntry()
    {
        var pool = new KeyedGatePool<Guid>();
        var key = Guid.NewGuid();

        var first = await pool.AcquireAsync(key, CancellationToken.None);
        pool.ActiveEntryCount.Should().Be(1);
        first.Dispose();

        pool.ActiveEntryCount.Should().Be(0, "the last release must retire the key's entry");

        // A fresh acquisition after retirement mints a working replacement entry.
        var second = await pool.AcquireAsync(key, CancellationToken.None);
        pool.ActiveEntryCount.Should().Be(1);
        second.Dispose();
        pool.ActiveEntryCount.Should().Be(0);
    }

    [Fact]
    public async Task Acquire_SerializesTheSameKeyAndKeepsTheEntryWhileAwaited()
    {
        var pool = new KeyedGatePool<Guid>();
        var key = Guid.NewGuid();

        var holder = await pool.AcquireAsync(key, CancellationToken.None);
        var waiter = pool.AcquireAsync(key, CancellationToken.None);
        await Task.Delay(50);
        waiter.IsCompleted.Should().BeFalse("the second acquisition must wait for the holder");
        pool.ActiveEntryCount.Should().Be(1, "holder and waiter share one live entry");

        holder.Dispose();
        var second = await waiter;
        pool.ActiveEntryCount.Should().Be(1, "the waiter's reference keeps the entry alive");
        second.Dispose();
        pool.ActiveEntryCount.Should().Be(0);
    }

    [Fact]
    public async Task Acquire_CanceledWhileWaiting_DropsItsReference()
    {
        var pool = new KeyedGatePool<Guid>();
        var key = Guid.NewGuid();

        var holder = await pool.AcquireAsync(key, CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var waiter = pool.AcquireAsync(key, cts.Token);
        cts.Cancel();
        await FluentActions.Awaiting(() => waiter).Should().ThrowAsync<OperationCanceledException>();

        holder.Dispose();
        pool.ActiveEntryCount.Should().Be(0, "a canceled waiter must not leak its reference");
    }

    [Fact]
    public async Task Acquire_ManyDistinctKeys_LeavesNothingBehind()
    {
        var pool = new KeyedGatePool<Guid>();
        for (var i = 0; i < 1000; i++)
        {
            var releaser = await pool.AcquireAsync(Guid.NewGuid(), CancellationToken.None);
            releaser.Dispose();
        }

        pool.ActiveEntryCount.Should().Be(0, "the pool must not grow with the touched-key universe");
    }
}
