using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Guards the shared projection cache against the empty-master window a warm or rebuild used to
/// expose, and against losing a concurrent upsert to the replacement that follows it.
/// <see cref="SecurityMasterProjectionCache"/> is registered as a singleton;
/// <see cref="SecurityMasterProjectionCache.ReplaceAll"/> runs on startup warms and
/// publish-triggered rebuilds while <see cref="SecurityMasterProjectionCache.Upsert"/> runs on
/// create, amend, and published-revision paths, and no caller serializes the two. Readers are
/// concurrent with both by construction.
/// </summary>
/// <remarks>
/// Each concurrency test drives <c>ReplaceAll</c> with a <see cref="PausingCollection"/> that runs a
/// callback partway through the fill, so the interleaving under test is forced rather than hoped
/// for. Two properties are wanted of every test here, and the second is the easier one to lose: it
/// must pass against the current implementation, and it must <em>fail</em> against the implementation
/// it exists to reject. A test that only spins a thread and hopes it lands in the window satisfies
/// the first and not the second.
/// <para>
/// <see cref="PausingCollection"/> is an <see cref="IReadOnlyCollection{T}"/> on purpose: that is
/// what real callers pass, and it is the shape <c>ReplaceAll</c> fills from inside its write gate,
/// so the pause lands in the window the assertions are about rather than in the defensive
/// materialization path a bare iterator would take.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SecurityMasterProjectionCacheTests
{
    private const int RecordCount = 250;

    /// <summary>
    /// How long a replacement holds its window open waiting to see whether an ungated write slips
    /// through. Under the current implementation this always expires — the writer cannot proceed
    /// until the swap releases the gate — so it costs one test roughly a second and never flakes.
    /// An implementation that lets the write through completes it far inside this budget, since the
    /// writer thread is already running and has one statement left to execute.
    /// </summary>
    private static readonly TimeSpan UngatedWriteGrace = TimeSpan.FromSeconds(1);

    [Fact]
    public void ReplaceAll_SwapsTheWholeMaster()
    {
        var cache = new SecurityMasterProjectionCache();
        var first = Record("FIRST-1");
        var second = Record("SECOND-1");

        cache.ReplaceAll([first]);
        cache.ReplaceAll([second]);

        cache.Count.Should().Be(1);
        cache.Get(second.SecurityId).Should().NotBeNull();
        cache.Get(first.SecurityId).Should().BeNull(
            "ReplaceAll substitutes the master rather than merging into it");
    }

    [Fact]
    public void Snapshot_TakenDuringReplaceAll_NeverObservesAPartialMaster()
    {
        var cache = new SecurityMasterProjectionCache();
        cache.ReplaceAll(Build("GEN-A"));

        using var replacementAtMidpoint = new ManualResetEventSlim(false);
        using var snapshotTaken = new ManualResetEventSlim(false);
        var observed = -1;

        var reader = StartThread("projection-cache-reader", () =>
        {
            replacementAtMidpoint.Wait();
            // Reads take no lock, so this lands while the replacement is mid-fill.
            observed = cache.Snapshot().Count;
            snapshotTaken.Set();
        });

        cache.ReplaceAll(new PausingCollection(Build("GEN-B"), () =>
        {
            replacementAtMidpoint.Set();
            snapshotTaken.Wait();
        }));

        reader.Join(TimeSpan.FromSeconds(30)).Should().BeTrue("the reader must not be stuck");
        observed.Should().Be(RecordCount,
            "a reader concurrent with ReplaceAll must see a complete master — a Clear()-then-refill "
            + "would have shown zero or a partial count at this exact point");
        cache.Count.Should().Be(RecordCount);
    }

    [Fact]
    public void Upsert_DuringReplaceAll_SurvivesTheSwap()
    {
        var cache = new SecurityMasterProjectionCache();
        cache.ReplaceAll(Build("GEN-A"));
        var late = Record("LATE-ARRIVAL");

        using var replacementAtMidpoint = new ManualResetEventSlim(false);
        using var writerAboutToUpsert = new ManualResetEventSlim(false);
        using var upsertReturned = new ManualResetEventSlim(false);

        var writer = StartThread("projection-cache-writer", () =>
        {
            replacementAtMidpoint.Wait();
            writerAboutToUpsert.Set();
            cache.Upsert(late);
            upsertReturned.Set();
        });

        cache.ReplaceAll(new PausingCollection(Build("GEN-B"), () =>
        {
            replacementAtMidpoint.Set();

            // The writer's very next statement is the Upsert, so from here on it is either blocked
            // on the write gate or has already written. An implementation that does not gate the
            // write lets it complete inside this window and then discards it at the swap — the
            // regression this test exists to reject. Asserting here rather than only on the final
            // read is what makes the rejection independent of how the writer is scheduled.
            writerAboutToUpsert.Wait();
            upsertReturned.Wait(UngatedWriteGrace).Should().BeFalse(
                "Upsert must not complete while a replacement holds the write gate — a write that "
                + "lands in the outgoing map is discarded by the swap");
        }));

        writer.Join(TimeSpan.FromSeconds(30)).Should().BeTrue("the upsert must not be stuck");
        cache.Get(late.SecurityId).Should().NotBeNull(
            "a record persisted by create, amend, or a published rebuild must not be discarded by a "
            + "replacement that was already in flight when it was written");
        cache.Count.Should().Be(RecordCount + 1);
    }

    [Fact]
    public void Upsert_WithAnOlderVersion_DoesNotDowngradeTheInstalledRecord()
    {
        var cache = new SecurityMasterProjectionCache();
        var securityId = Guid.NewGuid();
        var rebuilt = Record("SEC-1", securityId, version: 7);
        var stale = Record("SEC-1", securityId, version: 6);

        cache.ReplaceAll([rebuilt]);
        cache.Upsert(stale);

        cache.Get(securityId)!.Version.Should().Be(7,
            "a caller can produce its projection and then wait on the write gate, so the record it "
            + "holds may be older than one a rebuild installed meanwhile");
    }

    [Fact]
    public void Upsert_WithANewerVersion_ReplacesTheInstalledRecord()
    {
        var cache = new SecurityMasterProjectionCache();
        var securityId = Guid.NewGuid();

        cache.ReplaceAll([Record("SEC-1", securityId, version: 7)]);
        cache.Upsert(Record("SEC-1", securityId, version: 8));

        cache.Get(securityId)!.Version.Should().Be(8,
            "the version check must not turn Upsert into a no-op for genuinely newer records");
    }

    [Fact]
    public void Upsert_AfterReplaceAll_LandsInTheCurrentMaster()
    {
        var cache = new SecurityMasterProjectionCache();
        var seeded = Record("SEED-1");
        var added = Record("ADDED-1");

        cache.ReplaceAll([seeded]);
        cache.Upsert(added);

        cache.Count.Should().Be(2);
        cache.Get(added.SecurityId).Should().NotBeNull(
            "Upsert must write into the map the swap installed, not a stale reference");
    }

    /// <summary>
    /// A replacement set that runs <paramref name="atMidpoint"/> halfway through being enumerated,
    /// so the other thread's work is forced to interleave with the replacement instead of racing it.
    /// </summary>
    private sealed class PausingCollection(
        IReadOnlyList<SecurityProjectionRecord> records,
        Action atMidpoint) : IReadOnlyCollection<SecurityProjectionRecord>
    {
        public int Count => records.Count;

        public IEnumerator<SecurityProjectionRecord> GetEnumerator()
        {
            for (var i = 0; i < records.Count; i++)
            {
                if (i == records.Count / 2)
                {
                    atMidpoint();
                }

                yield return records[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // A dedicated thread rather than Task.Run: the replacement blocks waiting for this thread while
    // holding the write gate, so borrowing a pool thread would risk waiting on a queue that cannot
    // drain.
    private static Thread StartThread(string name, ThreadStart body)
    {
        var thread = new Thread(body) { IsBackground = true, Name = name };
        thread.Start();
        return thread;
    }

    private static IReadOnlyList<SecurityProjectionRecord> Build(string prefix)
        => Enumerable.Range(0, RecordCount).Select(i => Record($"{prefix}-{i}")).ToArray();

    private static SecurityProjectionRecord Record(
        string internalCode,
        Guid? securityId = null,
        long version = 1)
        => new(
            securityId ?? Guid.NewGuid(),
            "Equity",
            SecurityStatusDto.Active,
            internalCode,
            "USD",
            SecurityIdentifierKind.InternalCode.ToString(),
            internalCode,
            JsonSerializer.SerializeToElement(new { displayName = internalCode, currency = "USD" }),
            JsonSerializer.SerializeToElement(new { shareClass = "A" }),
            JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "projection-cache-tests",
                asOf = "2026-01-01T00:00:00+00:00",
                updatedBy = "projection-cache-tests"
            }),
            version,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            Array.Empty<SecurityIdentifierDto>(),
            Array.Empty<SecurityAliasDto>());
}
