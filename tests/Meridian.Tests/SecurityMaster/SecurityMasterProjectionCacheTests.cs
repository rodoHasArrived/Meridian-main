using System;
using System.Collections.Concurrent;
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
/// expose. <see cref="SecurityMasterProjectionCache"/> is registered as a singleton and
/// <c>ReplaceAll</c> runs on publish-triggered rebuilds and startup warms, so query readers are
/// concurrent with it by construction: a reader that lands mid-replace must still see a complete
/// master, not zero rows or a partially rebuilt set.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterProjectionCacheTests
{
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
        const int recordCount = 250;
        const int replacements = 40;

        var cache = new SecurityMasterProjectionCache();
        var generationA = Enumerable.Range(0, recordCount).Select(i => Record($"GEN-A-{i}")).ToArray();
        var generationB = Enumerable.Range(0, recordCount).Select(i => Record($"GEN-B-{i}")).ToArray();
        cache.ReplaceAll(generationA);

        var observedCounts = new ConcurrentBag<int>();
        using var stop = new CancellationTokenSource();

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                observedCounts.Add(cache.Snapshot().Count);
            }

            // One final read after the last replacement has certainly landed.
            observedCounts.Add(cache.Snapshot().Count);
        });

        for (var i = 0; i < replacements; i++)
        {
            cache.ReplaceAll(i % 2 == 0 ? generationB : generationA);
        }

        stop.Cancel();
        reader.GetAwaiter().GetResult();

        // Under the previous Clear()-then-refill implementation a reader routinely caught counts
        // between 0 and recordCount. With a reference swap every observation is a complete master.
        observedCounts.Should().NotBeEmpty();
        observedCounts.Should().OnlyContain(count => count == recordCount,
            "a reader concurrent with ReplaceAll must see a complete master, never an empty or partly filled one");
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
