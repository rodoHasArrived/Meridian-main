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
/// expose, and against a stale upsert reverting a record a rebuild installed.
/// <see cref="SecurityMasterProjectionCache"/> is registered as a singleton;
/// <see cref="SecurityMasterProjectionCache.ReplaceAll"/> runs on startup warms and
/// publish-triggered rebuilds while <see cref="SecurityMasterProjectionCache.Upsert"/> runs on
/// create, amend, and published-revision paths, and no caller serializes the two. Readers are
/// concurrent with both by construction.
/// </summary>
/// <remarks>
/// Two properties are wanted of every test here, and the second is the easier one to lose: it must
/// pass against the current implementation, and it must <em>fail</em> against the implementation it
/// exists to reject. A test that spins a thread and hopes it lands in the right window satisfies the
/// first and not the second.
/// <para>
/// <see cref="Snapshot_TakenDuringReplaceAll_NeverObservesAPartialMaster"/> drives <c>ReplaceAll</c>
/// with a <see cref="PausingCollection"/> that parks partway through being read, which is what makes
/// the interleaving forced rather than hoped for.
/// </para>
/// <para>
/// <strong>Not covered here, deliberately:</strong> that an upsert arriving while <c>ReplaceAll</c>
/// holds its write gate lands in the installed map. <c>ReplaceAll</c> copies its argument to an
/// array <em>before</em> taking the gate — it has to, or a lazily-enumerating source would run under
/// the gate and deadlock against the very writer this would test — so the only work left inside the
/// gate is an in-memory fill over an array, with no seam a caller can pause. That window is real but
/// not externally observable, and a test that cannot force the interleaving would pass whether or
/// not the gate exists. The neighbouring guarantee that <em>is</em> observable — that a stale upsert
/// cannot downgrade an installed record — is covered below.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public sealed class SecurityMasterProjectionCacheTests
{
    private const int RecordCount = 250;

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
            // Reads take no lock, so this lands while ReplaceAll is partway through reading its
            // replacement set and has not touched the master yet.
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

    // A dedicated thread rather than Task.Run: the replacement blocks waiting for this thread, so
    // borrowing a pool thread would risk waiting on a queue that cannot drain.
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
