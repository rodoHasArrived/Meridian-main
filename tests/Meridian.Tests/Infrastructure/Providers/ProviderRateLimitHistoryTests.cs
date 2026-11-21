using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Archival;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class ProviderRateLimitHistoryTests
{
    [Fact]
    public void Read_RetainsOnlyBoundedNewestEventsAndFiltersProvider()
    {
        var history = new ProviderRateLimitHistory(2);
        history.Record("polygon", null);
        history.Record("tiingo", null);
        history.Record("polygon", null, "stooq", 429);
        var snapshot = history.Read("POLYGON", 24);
        snapshot.Should().ContainSingle();
        snapshot[0].EventType.Should().Be("rotated");
        snapshot[0].NextProvider.Should().Be("stooq");
        snapshot[0].HttpStatus.Should().Be(429);
        history.Read("tiingo", 24).Should().ContainSingle();
        history.Read("missing", 24).Should().BeEmpty();
    }

    [Fact]
    public void Read_DoesNotInventHttpStatusForUnknownThrottle()
    {
        var history = new ProviderRateLimitHistory();
        history.Record("polygon", null);
        history.Read("polygon", 24)[0].HttpStatus.Should().BeNull();
        history.Read("polygon", 1, DateTimeOffset.UtcNow.AddHours(2)).Should().BeEmpty();
    }

    [Fact]
    public void ConcurrentWriters_KeepCapacityAndCoherentSnapshots()
    {
        var history = new ProviderRateLimitHistory(10);
        Parallel.For(0, 1000, _ => history.Record("polygon", null));
        history.Read("polygon", 24).Should().HaveCount(10);
    }

    [Fact]
    public void DailyRollups_SurviveRestartAndRetainSevenUtcDates()
    {
        var directory = Directory.CreateTempSubdirectory("mdc-rate-rollups-");
        try
        {
            var clock = new RequestBudgetTimeProvider();
            var writer = new AtomicFileWriterAdapter();
            var initial = new ProviderRateLimitHistory(directory.FullName, writer, clock, capacity: 1);
            initial.Record("polygon", null);
            clock.Advance(TimeSpan.FromDays(1));
            initial.Record("polygon", null);
            initial.Record("POLYGON", null, nextProvider: "stooq");
            initial.Record("tiingo", null);

            var restarted = new ProviderRateLimitHistory(directory.FullName, writer, clock);
            Assert.Empty(restarted.Read("polygon", 168));
            Assert.True(restarted.TryReadDailyRollups("polygon", out var days));
            Assert.Equal(new DateOnly(2026, 11, 12), days[0].DateUtc);
            Assert.Equal(1, days[0].ThrottleCount);
            Assert.Equal(1, days[0].RotationCount);
            Assert.Equal(2, days.Count);

            clock.Advance(TimeSpan.FromDays(6));
            restarted.Record("polygon", null);
            Assert.True(restarted.TryReadDailyRollups("polygon", out days));
            Assert.Equal([new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 12)], days.Select(day => day.DateUtc));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DailyRollups_ConcurrentInstancesRetainAllObservedCounts()
    {
        var directory = Directory.CreateTempSubdirectory("mdc-rate-rollups-");
        try
        {
            var clock = new RequestBudgetTimeProvider();
            var writer = new AtomicFileWriterAdapter();
            var first = new ProviderRateLimitHistory(directory.FullName, writer, clock);
            var second = new ProviderRateLimitHistory(directory.FullName, writer, clock);
            Parallel.Invoke(
                () => { for (var i = 0; i < 10; i++) first.Record("polygon", null); },
                () => { for (var i = 0; i < 12; i++) second.Record("polygon", null, nextProvider: "stooq"); });

            Assert.True(first.TryReadDailyRollups("polygon", out var days));
            var day = Assert.Single(days);
            Assert.Equal(10, day.ThrottleCount);
            Assert.Equal(12, day.RotationCount);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void DailyRollupWriteFailureKeepsRecentEvidenceAndReportsIncompleteTotals()
    {
        var directory = Directory.CreateTempSubdirectory("mdc-rate-rollups-");
        try
        {
            var metadataPath = Path.Combine(directory.FullName, "_meta");
            File.WriteAllText(metadataPath, "Blocks the rollup directory");
            var history = new ProviderRateLimitHistory(directory.FullName, new AtomicFileWriterAdapter());

            history.Record("polygon", null);

            Assert.Single(history.Read("polygon", 24));
            Assert.False(history.TryReadDailyRollups("polygon", out var days));
            Assert.Empty(days);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
