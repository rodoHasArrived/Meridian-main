using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
/// The concurrency tests are deterministic rather than timing-dependent. Each drives
/// <c>ReplaceAll</c> with <see cref="PausingCollection"/>, which parks mid-enumeration until the
/// other thread has done its work, so the interleaving under test is forced instead of hoped for —
/// a scheduling-dependent version would pass against the very implementations these tests exist to
/// reject. <see cref="PausingCollection"/> is an <see cref="IReadOnlyCollection{T}"/> on purpose:
/// that is the shape real callers pass, and it is the shape <c>ReplaceAll</c> fills from inside its
/// write gate, so the pause lands in the window the assertions are about.
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

        using var midpointReached = new ManualResetEventSlim(false);
        using var snapshotTaken = new ManualResetEventSlim(false);
        var observed = -1;

        var reader = Task.Run(() =>
        {
            midpointReached.Wait();
            // Reads take no lock, so this lands while the replacement is mid-fill.
            observed = cache.Snapshot().Count;
            snapshotTaken.Set();
        });

        cache.ReplaceAll(new PausingCollection(Build("GEN-B"), midpointReached, snapshotTaken));
        reader.GetAwaiter().GetResult();

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

        using var midpointReached = new ManualResetEventSlim(false);
        using var upsertIssued = new ManualResetEventSlim(false);

        var writer = Task.Run(() =>
        {
            midpointReached.Wait();
            upsertIssued.Set();
            // Blocks on the write gate the replacement holds, then resolves the map after the swap.
            cache.Upsert(late);
        });

        cache.ReplaceAll(new PausingCollection(Build("GEN-B"), midpointReached, upsertIssued));
        writer.GetAwaiter().GetResult();

        cache.Get(late.SecurityId).Should().NotBeNull(
            "a record persisted by create, amend, or a published rebuild must not be discarded by a "
            + "replacement that was already in flight when it was written");
        cache.Count.Should().Be(RecordCount + 1);
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
    /// A replacement set that releases <paramref name="midpointReached"/> halfway through being
    /// enumerated and then blocks on <paramref name="resume"/>, forcing the other thread's work to
    /// interleave with the replacement instead of racing it.
    /// </summary>
    private sealed class PausingCollection(
        IReadOnlyList<SecurityProjectionRecord> records,
        ManualResetEventSlim midpointReached,
        ManualResetEventSlim resume) : IReadOnlyCollection<SecurityProjectionRecord>
    {
        public int Count => records.Count;

        public IEnumerator<SecurityProjectionRecord> GetEnumerator()
        {
            for (var i = 0; i < records.Count; i++)
            {
                if (i == records.Count / 2)
                {
                    midpointReached.Set();
                    resume.Wait();
                }

                yield return records[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static IReadOnlyList<SecurityProjectionRecord> Build(string prefix)
        => Enumerable.Range(0, RecordCount).Select(i => Record($"{prefix}-{i}")).ToArray();

    private static SecurityProjectionRecord Record(string internalCode)
        => new(
            Guid.NewGuid(),
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
            1,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            Array.Empty<SecurityIdentifierDto>(),
            Array.Empty<SecurityAliasDto>());
}
