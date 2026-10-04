using System.Text.Json;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Serialization;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.Backfill;
using Meridian.Testing;

namespace Meridian.Tests.Storage;

public sealed class JsonlBackfillBarWriterTests
{
    [Theory]
    [InlineData(DataGranularity.Daily, "daily")]
    [InlineData(DataGranularity.Hour1, "hourly")]
    [InlineData(DataGranularity.Minute5, "5min")]
    public async Task WriteAsync_PreservesPartitionAndAppendsSerializedBars(DataGranularity granularity, string prefix)
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(WriteAsync_PreservesPartitionAndAppendsSerializedBars));
        IBackfillBarWriter writer = new JsonlBackfillBarWriter(artifacts.RootPath);
        var first = new HistoricalBar("SPY", new DateOnly(2026, 7, 1), 100, 105, 99, 103, 500, "alpaca", 1);
        var second = new HistoricalBar("SPY", first.SessionDate, 103, 106, 102, 104, 600, "alpaca", 2);

        var path = await writer.WriteAsync(granularity, [first], "composite");
        var appendedPath = await writer.WriteAsync(granularity, [second], "composite");

        Assert.Equal(Path.Combine(artifacts.RootPath, "SPY", "HistoricalBar", $"bar_{prefix}_2026-07-01.jsonl"), path);
        Assert.Equal(path, appendedPath);
        var lines = await File.ReadAllLinesAsync(path);
        Assert.Equal(2, lines.Length);
        Assert.Equal(first, JsonSerializer.Deserialize(lines[0], MarketDataJsonContext.Default.HistoricalBar));
        Assert.Equal(second, JsonSerializer.Deserialize(lines[1], MarketDataJsonContext.Default.HistoricalBar));
    }

    [Fact]
    public async Task WriteAsync_CancelledBatch_DoesNotChangeCommittedFile()
    {
        using var artifacts = TestArtifactDirectory.Create(nameof(WriteAsync_CancelledBatch_DoesNotChangeCommittedFile));
        IBackfillBarWriter writer = new JsonlBackfillBarWriter(artifacts.RootPath);
        var bar = new HistoricalBar("SPY", new DateOnly(2026, 7, 1), 100, 105, 99, 103, 500);
        var path = await writer.WriteAsync(DataGranularity.Daily, [bar], "stooq");
        var committed = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            writer.WriteAsync(DataGranularity.Daily, [bar], "stooq", cancellation.Token));

        Assert.Equal(committed, await File.ReadAllBytesAsync(path));
    }
}
