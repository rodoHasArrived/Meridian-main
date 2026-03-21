using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage.Replay;
using Xunit;

namespace Meridian.Tests.Storage;

public class MemoryMappedJsonlReaderTests : IDisposable
{
    private readonly string _testRoot;

    public MemoryMappedJsonlReaderTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"mdc_mmf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    [Fact]
    public async Task ReadEventsAsync_WithEmptyDirectory_ReturnsNoEvents()
    {
        // Arrange
        var reader = new MemoryMappedJsonlReader(_testRoot);

        // Act
        var events = new List<MarketEvent>();
        await foreach (var evt in reader.ReadEventsAsync())
        {
            events.Add(evt);
        }

        // Assert
        events.Should().BeEmpty();
        reader.FilesRead.Should().Be(0);
    }

    [Fact]
    public async Task ReadEventsAsync_WithNonExistentDirectory_ReturnsNoEvents()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testRoot, "nonexistent");
        var reader = new MemoryMappedJsonlReader(nonExistentPath);

        // Act
        var events = new List<MarketEvent>();
        await foreach (var evt in reader.ReadEventsAsync())
        {
            events.Add(evt);
        }

        // Assert
        events.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadEventsAsync_WithSmallFile_UsesStreamingMode()
    {
        // Arrange
        await CreateTestJsonlFileAsync("small.jsonl", 10);

        var options = new MemoryMappedReaderOptions { MinFileSizeForMapping = 1024 * 1024 }; // 1MB threshold
        var reader = new MemoryMappedJsonlReader(_testRoot, options);

        // Act
        var events = new List<MarketEvent>();
        await foreach (var evt in reader.ReadEventsAsync())
        {
            events.Add(evt);
        }

        // Assert
        events.Should().HaveCount(10);
        reader.FilesRead.Should().Be(1);
        reader.MemoryMappedFilesUsed.Should().Be(0); // Small file uses streaming
    }

    [Fact]
    public async Task ReadEventsAsync_WithLargeFile_UsesMemoryMapping()
    {
        // Arrange - Create a file larger than min size
        await CreateTestJsonlFileAsync("large.jsonl", 1000);

        var options = new MemoryMappedReaderOptions { MinFileSizeForMapping = 1024 }; // 1KB threshold
        var reader = new MemoryMappedJsonlReader(_testRoot, options);

        // Act
        var events = new List<MarketEvent>();
        await foreach (var evt in reader.ReadEventsAsync())
        {
            events.Add(evt);
        }

        // Assert
        events.Should().HaveCount(1000);
        reader.FilesRead.Should().Be(1);
        reader.MemoryMappedFilesUsed.Should().Be(1);
    }

    [Fact]
    public async Task ReadEventsAsync_WithCompressedFile_UsesStreamingMode()
    {
        // Arrange
        await CreateCompressedJsonlFileAsync("compressed.jsonl.gz", 50);

        var options = new MemoryMappedReaderOptions { MinFileSizeForMapping = 1 }; // Force mapping for uncompressed
        var reader = new MemoryMappedJsonlReader(_testRoot, options);

        // Act
        var events = new List<MarketEvent>();
        await foreach (var evt in reader.ReadEventsAsync())
        {
            events.Add(evt);
        }

        // Assert
        events.Should().HaveCount(50);
        reader.FilesRead.Should().Be(1);
        reader.MemoryMappedFilesUsed.Should().Be(0); // Compressed files use streaming
    }

    [Fact]
    public async Task ReadEventsAsync_WithMultipleFiles_ReadsAllInOrder()
    {
        // Arrange
        await CreateTestJsonlFileAsync("a_first.jsonl", 10);
        await CreateTestJsonlFileAsync("b_second.jsonl", 20);
        await CreateTestJsonlFileAsync("c_third.jsonl", 15);

        var reader = new MemoryMappedJsonlReader(_testRoot);

        // Act
        var events = new List<MarketEvent>();
        await foreach (var evt in reader.ReadEventsAsync())
        {
            events.Add(evt);
        }

        // Assert
        events.Should().HaveCount(45);
        reader.FilesRead.Should().Be(3);
    }

    [Fact]
    public async Task ReadEventsBatchedAsync_ReturnsBatches()
    {
        // Arrange
        await CreateTestJsonlFileAsync("test.jsonl", 25);

        var reader = new MemoryMappedJsonlReader(_testRoot);

        // Act
        var batches = new List<IReadOnlyList<MarketEvent>>();
        await foreach (var batch in reader.ReadEventsBatchedAsync(10))
        {
            batches.Add(batch);
        }

        // Assert
        batches.Should().HaveCount(3);
        batches[0].Should().HaveCount(10);
        batches[1].Should().HaveCount(10);
        batches[2].Should().HaveCount(5);
    }

    [Fact]
    public async Task ReadEventsInRangeAsync_FiltersCorrectly()
    {
        // Arrange
        var baseTime = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);
        await CreateTestJsonlFileAsync("test.jsonl", 100, baseTime);

        var reader = new MemoryMappedJsonlReader(_testRoot);
        var from = baseTime.AddSeconds(10);
        var to = baseTime.AddSeconds(30);

        // Act
        var events = new List<MarketEvent>();
        await foreach (var evt in reader.ReadEventsInRangeAsync(from, to))
        {
            events.Add(evt);
        }

        // Assert
        events.Should().HaveCount(21); // Inclusive range from 10 to 30
        events.All(e => e.Timestamp >= from && e.Timestamp <= to).Should().BeTrue();
    }

    [Fact]
    public async Task ReadEventsForSymbolsAsync_FiltersCorrectly()
    {
        // Arrange
        await CreateMultiSymbolJsonlFileAsync("multi.jsonl", new[] { "AAPL", "MSFT", "GOOG" }, 10);

        var reader = new MemoryMappedJsonlReader(_testRoot);
        var targetSymbols = new HashSet<string> { "AAPL", "GOOG" };

        // Act
        var events = new List<MarketEvent>();
        await foreach (var evt in reader.ReadEventsForSymbolsAsync(targetSymbols))
        {
            events.Add(evt);
        }

        // Assert
        events.Should().HaveCount(20); // 10 AAPL + 10 GOOG
        events.All(e => targetSymbols.Contains(e.Symbol)).Should().BeTrue();
    }

    [Fact]
    public async Task ReadFileAsync_WithMalformedJson_SkipsInvalidLines()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "mixed.jsonl");
        var sb = new StringBuilder();

        // Add valid event
        sb.AppendLine(CreateEventJson("AAPL", 1));
        // Add malformed JSON
        sb.AppendLine("{ invalid json }");
        // Add another valid event
        sb.AppendLine(CreateEventJson("AAPL", 2));

        await File.WriteAllTextAsync(filePath, sb.ToString());

        var reader = new MemoryMappedJsonlReader(_testRoot);

        // Act
        var events = new List<MarketEvent>();
        await foreach (var evt in reader.ReadEventsAsync())
        {
            events.Add(evt);
        }

        // Assert
        events.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReadEventsAsync_WithCancellation_StopsReading()
    {
        // Arrange - Create a larger file that will be processed in batches
        // This ensures cancellation is checked between batches
        await CreateTestJsonlFileAsync("test.jsonl", 2000);

        var reader = new MemoryMappedJsonlReader(_testRoot);
        var cts = new CancellationTokenSource();
        int targetEventsBeforeCancel = 100; // Cancel after reading some events

        // Act
        var events = new List<MarketEvent>();
        bool exceptionWasThrown = false;

        try
        {
            await foreach (var evt in reader.ReadEventsAsync(cts.Token))
            {
                events.Add(evt);
                if (events.Count == targetEventsBeforeCancel)
                {
                    cts.Cancel();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is observed in iterator
            exceptionWasThrown = true;
        }

        // Assert - Cancellation should have stopped reading
        // Either by throwing OperationCanceledException or by stopping iteration
        // In both cases, should have read significantly fewer than 2000 events
        events.Should().NotBeEmpty("should have read some events before cancellation");
        events.Should().HaveCountLessThan(2000, "should stop reading after cancellation was requested");

        // The test is valid whether exception was thrown or iterator just stopped
        // Both behaviors correctly respond to cancellation
        var cancellationWasObserved = exceptionWasThrown || events.Count < 2000;
        cancellationWasObserved.Should().BeTrue("cancellation should have been observed by stopping iteration or throwing");
    }

    [Fact]
    public void GetFileStatistics_ReturnsCorrectStatistics()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_testRoot, "file1.jsonl"), "{}");
        File.WriteAllText(Path.Combine(_testRoot, "file2.jsonl"), "{}");
        using (var fs = File.Create(Path.Combine(_testRoot, "file3.jsonl.gz")))
        using (var gz = new GZipStream(fs, CompressionMode.Compress))
        {
            gz.Write("{}"u8);
        }

        var reader = new MemoryMappedJsonlReader(_testRoot);

        // Act
        var stats = reader.GetFileStatistics();

        // Assert
        stats.TotalFiles.Should().Be(3);
        stats.UncompressedFiles.Should().Be(2);
        stats.CompressedFiles.Should().Be(1);
    }

    [Fact]
    public void MemoryMappedReaderOptions_DefaultPresets_HaveCorrectValues()
    {
        // Default
        var defaultOpts = MemoryMappedReaderOptions.Default;
        defaultOpts.ChunkSize.Should().Be(4 * 1024 * 1024);
        defaultOpts.BatchSize.Should().Be(1000);
        defaultOpts.MinFileSizeForMapping.Should().Be(1024 * 1024);

        // High Throughput
        var highThroughput = MemoryMappedReaderOptions.HighThroughput;
        highThroughput.ChunkSize.Should().Be(16 * 1024 * 1024);
        highThroughput.BatchSize.Should().Be(5000);

        // Low Memory
        var lowMemory = MemoryMappedReaderOptions.LowMemory;
        lowMemory.ChunkSize.Should().Be(1024 * 1024);
        lowMemory.BatchSize.Should().Be(500);
    }

    [Fact]
    public async Task ReadEventsAsync_WithParallelDeserialization_HandlesLargeBatches()
    {
        // Arrange
        await CreateTestJsonlFileAsync("large.jsonl", 500);

        var options = new MemoryMappedReaderOptions
        {
            BatchSize = 200,
            UseParallelDeserialization = true,
            ParallelDeserializationThreshold = 50,
            MinFileSizeForMapping = 1 // Force memory mapping
        };
        var reader = new MemoryMappedJsonlReader(_testRoot, options);

        // Act
        var events = new List<MarketEvent>();
        await foreach (var evt in reader.ReadEventsAsync())
        {
            events.Add(evt);
        }

        // Assert
        events.Should().HaveCount(500);
        reader.EventsRead.Should().Be(500);
    }

    [Fact]
    public async Task ReadEventsAsync_TracksMetrics()
    {
        // Arrange
        await CreateTestJsonlFileAsync("test.jsonl", 50);

        var reader = new MemoryMappedJsonlReader(_testRoot);

        // Act
        await foreach (var _ in reader.ReadEventsAsync())
        { }

        // Assert
        reader.FilesRead.Should().Be(1);
        reader.EventsRead.Should().Be(50);
        reader.BytesRead.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ReadEventsAsync_WithSubdirectories_ReadsRecursively()
    {
        // Arrange
        var subDir = Path.Combine(_testRoot, "2026", "01", "05");
        Directory.CreateDirectory(subDir);
        await CreateTestJsonlFileAsync(Path.Combine("2026", "01", "05", "test.jsonl"), 20);

        var reader = new MemoryMappedJsonlReader(_testRoot);

        // Act
        var events = new List<MarketEvent>();
        await foreach (var evt in reader.ReadEventsAsync())
        {
            events.Add(evt);
        }

        // Assert
        events.Should().HaveCount(20);
    }

    private async Task CreateTestJsonlFileAsync(string fileName, int eventCount, DateTimeOffset? baseTime = null)
    {
        var filePath = Path.Combine(_testRoot, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var sb = new StringBuilder();
        var time = baseTime ?? DateTimeOffset.UtcNow;

        for (int i = 0; i < eventCount; i++)
        {
            sb.AppendLine(CreateEventJson("AAPL", i, time.AddSeconds(i)));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString());
    }

    private async Task CreateCompressedJsonlFileAsync(string fileName, int eventCount)
    {
        var filePath = Path.Combine(_testRoot, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await using var fs = File.Create(filePath);
        await using var gz = new GZipStream(fs, CompressionLevel.Fastest);
        await using var writer = new StreamWriter(gz);

        for (int i = 0; i < eventCount; i++)
        {
            await writer.WriteLineAsync(CreateEventJson("AAPL", i));
        }
    }

    private async Task CreateMultiSymbolJsonlFileAsync(string fileName, string[] symbols, int eventsPerSymbol)
    {
        var filePath = Path.Combine(_testRoot, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var sb = new StringBuilder();

        foreach (var symbol in symbols)
        {
            for (int i = 0; i < eventsPerSymbol; i++)
            {
                sb.AppendLine(CreateEventJson(symbol, i));
            }
        }

        await File.WriteAllTextAsync(filePath, sb.ToString());
    }

    private static string CreateEventJson(string symbol, int sequence, DateTimeOffset? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow;
        var trade = new Trade(ts, symbol, 100m + sequence, 100, AggressorSide.Buy, sequence);
        var evt = MarketEvent.Trade(ts, symbol, trade, sequence, "TEST");
        return JsonSerializer.Serialize(evt, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
