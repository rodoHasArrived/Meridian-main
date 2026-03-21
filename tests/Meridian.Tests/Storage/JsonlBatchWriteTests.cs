using System.Threading;
using FluentAssertions;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Sinks;
using Xunit;

namespace Meridian.Tests.Storage;

public class JsonlBatchWriteTests : IDisposable
{
    private readonly string _testRoot;

    public JsonlBatchWriteTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"mdc_batch_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_testRoot))
            return;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(_testRoot, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(10);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(10);
            }
        }
    }

    [Fact]
    public async Task AppendAsync_WithBatchingDisabled_WritesImmediately()
    {
        // Arrange
        var options = new StorageOptions { RootPath = _testRoot };
        var policy = new TestStoragePolicy(_testRoot);
        await using var sink = new JsonlStorageSink(options, policy, JsonlBatchOptions.NoBatching);

        var evt = CreateTestEvent("AAPL", 1);

        // Act
        await sink.AppendAsync(evt);
        await sink.FlushAsync();

        // Assert
        sink.EventsWritten.Should().Be(1);
        sink.EventsBuffered.Should().Be(0);

        var files = Directory.GetFiles(_testRoot, "*.jsonl", SearchOption.AllDirectories);
        files.Should().HaveCount(1);
    }

    [Fact]
    public async Task AppendAsync_WithBatchingEnabled_BuffersUntilBatchSize()
    {
        // Arrange
        var batchOptions = new JsonlBatchOptions { BatchSize = 10, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        var options = new StorageOptions { RootPath = _testRoot };
        var policy = new TestStoragePolicy(_testRoot);
        await using var sink = new JsonlStorageSink(options, policy, batchOptions);

        // Act - Add 5 events (less than batch size)
        for (int i = 0; i < 5; i++)
        {
            await sink.AppendAsync(CreateTestEvent("AAPL", i));
        }

        // Assert - Events should be buffered, not written yet
        sink.EventsBuffered.Should().Be(5);
        sink.EventsWritten.Should().Be(0);
        sink.BatchesWritten.Should().Be(0);
    }

    [Fact]
    public async Task AppendAsync_WhenBatchSizeReached_FlushesAutomatically()
    {
        // Arrange
        var batchOptions = new JsonlBatchOptions { BatchSize = 5, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        var options = new StorageOptions { RootPath = _testRoot };
        var policy = new TestStoragePolicy(_testRoot);
        await using var sink = new JsonlStorageSink(options, policy, batchOptions);

        // Act - Add exactly batch size events
        for (int i = 0; i < 5; i++)
        {
            await sink.AppendAsync(CreateTestEvent("AAPL", i));
        }

        // Assert - Events should be flushed
        sink.EventsWritten.Should().Be(5);
        sink.EventsBuffered.Should().Be(0);
        sink.BatchesWritten.Should().Be(1);
    }

    [Fact]
    public async Task AppendAsync_MultipleBatches_WritesCorrectly()
    {
        // Arrange
        var batchOptions = new JsonlBatchOptions { BatchSize = 3, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        var options = new StorageOptions { RootPath = _testRoot };
        var policy = new TestStoragePolicy(_testRoot);
        await using var sink = new JsonlStorageSink(options, policy, batchOptions);

        // Act - Add 10 events (3 full batches + 1 remaining)
        for (int i = 0; i < 10; i++)
        {
            await sink.AppendAsync(CreateTestEvent("AAPL", i));
        }

        // Assert - 3 batches written, 1 event buffered
        sink.BatchesWritten.Should().Be(3);
        sink.EventsWritten.Should().Be(9);
        sink.EventsBuffered.Should().Be(1);

        // Flush remaining
        await sink.FlushAsync();
        sink.EventsWritten.Should().Be(10);
        sink.EventsBuffered.Should().Be(0);
    }

    [Fact]
    public async Task FlushAsync_FlushesPendingEvents()
    {
        // Arrange
        var batchOptions = new JsonlBatchOptions { BatchSize = 100, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        var options = new StorageOptions { RootPath = _testRoot };
        var policy = new TestStoragePolicy(_testRoot);
        await using var sink = new JsonlStorageSink(options, policy, batchOptions);

        // Add some events
        for (int i = 0; i < 5; i++)
        {
            await sink.AppendAsync(CreateTestEvent("AAPL", i));
        }

        // Verify buffered
        sink.EventsBuffered.Should().Be(5);

        // Act
        await sink.FlushAsync();

        // Assert
        sink.EventsWritten.Should().Be(5);
        sink.EventsBuffered.Should().Be(0);
    }

    [Fact]
    public async Task DisposeAsync_FlushesRemainingEvents()
    {
        // Arrange
        var batchOptions = new JsonlBatchOptions { BatchSize = 100, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        var options = new StorageOptions { RootPath = _testRoot };
        var policy = new TestStoragePolicy(_testRoot);
        var sink = new JsonlStorageSink(options, policy, batchOptions);

        for (int i = 0; i < 5; i++)
        {
            await sink.AppendAsync(CreateTestEvent("AAPL", i));
        }

        // Verify events are buffered before disposal
        sink.EventsBuffered.Should().Be(5, "events should be buffered before disposal");
        sink.EventsWritten.Should().Be(0, "no events should be written yet");

        // Act
        await sink.DisposeAsync();

        // Verify events were written during disposal
        sink.EventsWritten.Should().Be(5, "all events should be written after disposal");
        sink.EventsBuffered.Should().Be(0, "no events should be buffered after disposal");

        // Assert - File should contain all events
        var files = Directory.GetFiles(_testRoot, "*.jsonl", SearchOption.AllDirectories);
        files.Should().HaveCount(1, $"should find 1 file in {_testRoot}");

        var lines = await File.ReadAllLinesAsync(files[0]);
        lines.Should().HaveCount(5);
    }

    [Fact]
    public async Task AppendAsync_MultipleSymbols_UsesSeparateBuffers()
    {
        // Arrange
        var batchOptions = new JsonlBatchOptions { BatchSize = 5, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        var options = new StorageOptions { RootPath = _testRoot };
        var policy = new TestStoragePolicy(_testRoot);
        await using var sink = new JsonlStorageSink(options, policy, batchOptions);

        // Act - Add events for different symbols
        for (int i = 0; i < 3; i++)
        {
            await sink.AppendAsync(CreateTestEvent("AAPL", i));
            await sink.AppendAsync(CreateTestEvent("MSFT", i));
        }

        // Assert - Each symbol has separate buffer
        sink.EventsBuffered.Should().Be(6); // 3 AAPL + 3 MSFT

        await sink.FlushAsync();
        sink.EventsWritten.Should().Be(6);
    }

    [Fact]
    public async Task AppendAsync_WithParallelSerialization_HandlesLargeBatches()
    {
        // Arrange
        var batchOptions = new JsonlBatchOptions
        {
            BatchSize = 200,
            Enabled = true,
            ParallelSerializationThreshold = 50,
            FlushInterval = TimeSpan.FromMinutes(5)
        };
        var options = new StorageOptions { RootPath = _testRoot };
        var policy = new TestStoragePolicy(_testRoot);
        await using var sink = new JsonlStorageSink(options, policy, batchOptions);

        // Act - Add enough events to trigger parallel serialization
        for (int i = 0; i < 200; i++)
        {
            await sink.AppendAsync(CreateTestEvent("AAPL", i));
        }

        // Assert
        sink.BatchesWritten.Should().Be(1);
        sink.EventsWritten.Should().Be(200);
    }

    [Fact]
    public void JsonlBatchOptions_DefaultPresets_HaveCorrectValues()
    {
        // Default
        var defaultOpts = JsonlBatchOptions.Default;
        defaultOpts.BatchSize.Should().Be(1000);
        defaultOpts.FlushInterval.Should().Be(TimeSpan.FromSeconds(5));
        defaultOpts.Enabled.Should().BeTrue();

        // High Throughput
        var highThroughput = JsonlBatchOptions.HighThroughput;
        highThroughput.BatchSize.Should().Be(5000);
        highThroughput.FlushInterval.Should().Be(TimeSpan.FromSeconds(10));

        // Low Latency
        var lowLatency = JsonlBatchOptions.LowLatency;
        lowLatency.BatchSize.Should().Be(100);
        lowLatency.FlushInterval.Should().Be(TimeSpan.FromSeconds(1));

        // No Batching
        var noBatching = JsonlBatchOptions.NoBatching;
        noBatching.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task AppendAsync_FileContainsValidJson()
    {
        // Arrange
        var batchOptions = new JsonlBatchOptions { BatchSize = 5, Enabled = true, FlushInterval = TimeSpan.FromMinutes(5) };
        var options = new StorageOptions { RootPath = _testRoot };
        var policy = new TestStoragePolicy(_testRoot);
        var sink = new JsonlStorageSink(options, policy, batchOptions);

        // Act
        for (int i = 0; i < 5; i++)
        {
            await sink.AppendAsync(CreateTestEvent("AAPL", i));
        }

        // Dispose sink to release file handles before reading on Windows
        await sink.DisposeAsync();

        // Assert
        var files = Directory.GetFiles(_testRoot, "*.jsonl", SearchOption.AllDirectories);
        files.Should().HaveCount(1);

        var lines = await File.ReadAllLinesAsync(files[0]);
        foreach (var line in lines)
        {
            var action = () => System.Text.Json.JsonSerializer.Deserialize<MarketEvent>(line);
            action.Should().NotThrow();
        }
    }

    private static MarketEvent CreateTestEvent(string symbol, int sequence)
    {
        var trade = new Trade(
            DateTimeOffset.UtcNow,
            symbol,
            100m + sequence,
            100,
            AggressorSide.Buy,
            sequence);

        return MarketEvent.Trade(DateTimeOffset.UtcNow, symbol, trade, sequence, "TEST");
    }

    private sealed class TestStoragePolicy : IStoragePolicy
    {
        private readonly string _root;

        public TestStoragePolicy(string root) => _root = root;

        public string GetPath(MarketEvent evt)
        {
            return Path.Combine(_root, $"{evt.Symbol}_{evt.Timestamp:yyyyMMdd}.jsonl");
        }
    }
}
