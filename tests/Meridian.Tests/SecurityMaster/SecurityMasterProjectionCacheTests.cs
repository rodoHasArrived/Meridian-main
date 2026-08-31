using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The projection cache's <c>ReplaceAll</c> must swap in the replacement set ATOMICALLY: the
/// previous clear-then-fill implementation exposed an empty security master to any reader that
/// arrived between the clear and the repopulation, so a routine re-warm read as a mass delisting.
/// </summary>
public sealed class SecurityMasterProjectionCacheTests
{
    [Fact]
    public void ReplaceAll_SwapsAtomically_ReadersNeverObserveAnEmptyOrPartialMaster()
    {
        const int setSize = 50;
        var cache = new SecurityMasterProjectionCache();
        var setA = BuildSet(setSize);
        var setB = BuildSet(setSize);
        cache.ReplaceAll(setA);

        using var stop = new CancellationTokenSource();
        var observedCounts = new List<int>();
        var reader = Task.Run(() =>
        {
            while (!stop.Token.IsCancellationRequested)
            {
                var count = cache.Snapshot().Count;
                if (count != setSize)
                {
                    lock (observedCounts)
                    {
                        observedCounts.Add(count);
                    }
                }
            }
        });

        for (var i = 0; i < 200; i++)
        {
            cache.ReplaceAll(i % 2 == 0 ? setB : setA);
        }

        stop.Cancel();
        reader.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        observedCounts.Should().BeEmpty(
            "every reader must observe a COMPLETE set — an empty or partial count means the swap exposed the clear-then-fill window");
        cache.Count.Should().Be(setSize);
    }

    [Fact]
    public void UpsertGetRemove_RoundTrip()
    {
        var cache = new SecurityMasterProjectionCache();
        var record = BuildSet(1)[0];

        cache.Get(record.SecurityId).Should().BeNull();
        cache.Upsert(record);
        cache.Get(record.SecurityId).Should().BeSameAs(record);
        cache.Remove(record.SecurityId).Should().BeTrue();
        cache.Get(record.SecurityId).Should().BeNull();
        cache.Remove(record.SecurityId).Should().BeFalse();
    }

    private static IReadOnlyList<SecurityProjectionRecord> BuildSet(int size)
    {
        var records = new List<SecurityProjectionRecord>(size);
        for (var i = 0; i < size; i++)
        {
            var securityId = Guid.NewGuid();
            records.Add(new SecurityProjectionRecord(
                SecurityId: securityId,
                AssetClass: "Equity",
                Status: SecurityStatusDto.Active,
                DisplayName: $"Cache Test {i}",
                Currency: "USD",
                PrimaryIdentifierKind: "InternalCode",
                PrimaryIdentifierValue: $"CACHE-{i}",
                CommonTerms: JsonSerializer.SerializeToElement(new { displayName = $"Cache Test {i}", currency = "USD" }),
                AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
                Provenance: JsonSerializer.SerializeToElement(new
                {
                    sourceSystem = "cache-tests",
                    asOf = "2026-01-01T00:00:00+00:00",
                    updatedBy = "cache-tests",
                }),
                Version: 1,
                EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-1),
                EffectiveTo: null,
                Identifiers:
                [
                    new SecurityIdentifierDto(SecurityIdentifierKind.InternalCode, $"CACHE-{i}", true, DateTimeOffset.UtcNow.AddDays(-1))
                ],
                Aliases: Array.Empty<SecurityAliasDto>()));
        }

        return records;
    }
}
