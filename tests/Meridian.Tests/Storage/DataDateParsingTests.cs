using System.Reflection;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage;
using Meridian.Storage.Export;
using Meridian.Storage.Services;
using Xunit;

namespace Meridian.Tests.Storage;

// TZ and TimeZoneInfo's local-zone cache are process-wide, so these tests must
// run apart from every collection that could observe the temporary setting.
[Collection("Sequential")]
public sealed class DataDateParsingTests
{
    [Theory]
    [InlineData("Asia/Tokyo", "2024-01-15", 15)]
    [InlineData("America/New_York", "2024-01-15", 15)]
    [InlineData("Asia/Tokyo", "2024-01-15T00:30:00+09:00", 14)]
    [InlineData("America/New_York", "2024-01-15T23:30:00-05:00", 16)]
    public async Task GapInventory_PreservesCalendarDatesAndConvertsActualTimestampsToUtc(
        string timeZone, string timestamp, int expectedDay)
    {
        using var zone = new LocalTimeZoneScope(timeZone);
        using var directory = new TestDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "AAPL_bar.jsonl"),
            $$"""{"date":"{{timestamp}}"}""");

        var inventory = await new DataGapAnalyzer(directory.Path).GetSymbolInventoryAsync("AAPL");

        inventory.DailyBarDates.Should().ContainSingle().Which.Should().Be(new DateOnly(2024, 1, expectedDay));
    }

    [Theory]
    [InlineData("Asia/Tokyo")]
    [InlineData("America/New_York")]
    public void StorageSearch_DateOnlyQueryUsesUtcDayBoundaries(string timeZone)
    {
        using var zone = new LocalTimeZoneScope(timeZone);
        using var directory = new TestDirectory();
        var service = new StorageSearchService(new StorageOptions { RootPath = directory.Path });

        var query = service.ParseNaturalLanguageQuery("AAPL trades on 2024-01-15");

        query.Should().NotBeNull();
        query!.From.Should().Be(new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero));
        query.From!.Value.Offset.Should().Be(TimeSpan.Zero);
        query.To.Should().Be(query.From.Value.AddDays(1));
        query.To!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("Asia/Tokyo")]
    [InlineData("America/New_York")]
    public void ParquetTimestampConversion_PreservesDateOnlyAndUnspecifiedDateTimeAsUtc(string timeZone)
    {
        using var zone = new LocalTimeZoneScope(timeZone);
        var expected = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        object[] values =
        {
            expected,
            "2024-01-15",
            new DateTime(2024, 1, 15),
            expected.LocalDateTime,
            "2024-01-15T09:00:00+09:00"
        };
        var convert = typeof(AnalysisExportService).GetMethod("ConvertToDateTimeOffset",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        foreach (var value in values)
        {
            var timestamp = (DateTimeOffset?)convert.Invoke(null, [value]);
            timestamp.Should().Be(expected);
            timestamp!.Value.Offset.Should().Be(TimeSpan.Zero);
        }
    }

    private sealed class LocalTimeZoneScope : IDisposable
    {
        private readonly string? _previousTimeZone = Environment.GetEnvironmentVariable("TZ");

        public LocalTimeZoneScope(string timeZone)
        {
            Environment.SetEnvironmentVariable("TZ", timeZone);
            TimeZoneInfo.ClearCachedData();
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("TZ", _previousTimeZone);
            TimeZoneInfo.ClearCachedData();
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"meridian_dates_{Guid.NewGuid():N}");

        public TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
