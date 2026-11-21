using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Events;
using Xunit;

namespace Meridian.Tests.Application.Pipeline;

public sealed class DeadLetterSinkTests : IAsyncLifetime
{
    private string _testDir = null!;
    private DeadLetterSink _sink = null!;

    public Task InitializeAsync()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"deadletter_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _sink = new DeadLetterSink(_testDir);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _sink.DisposeAsync();
        try
        {
            Directory.Delete(_testDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task RecordAsync_WritesDeadLetterRecordToFile()
    {
        var evt = CreateTestEvent("TSLA");

        await _sink.RecordAsync(evt, ["schema_invalid", "missing_bid"]);

        var deadLetterFile = Path.Combine(_testDir, "_dead_letter", "rejected_events.jsonl");
        File.Exists(deadLetterFile).Should().BeTrue();

        var content = await File.ReadAllTextAsync(deadLetterFile);
        content.Should().Contain("TSLA");
        content.Should().Contain("schema_invalid");
        content.Should().Contain("missing_bid");
    }

    [Fact]
    public async Task RecordAsync_MultipleEvents_AppendsAllLinesAndTracksCounts()
    {
        await _sink.RecordAsync(CreateTestEvent("AAPL"), ["invalid_sequence"]);
        await _sink.RecordAsync(CreateTestEvent("AAPL"), ["invalid_payload"]);
        await _sink.RecordAsync(CreateTestEvent("MSFT"), ["invalid_source"]);

        var deadLetterFile = Path.Combine(_testDir, "_dead_letter", "rejected_events.jsonl");
        var lines = await File.ReadAllLinesAsync(deadLetterFile);

        lines.Where(static line => !string.IsNullOrWhiteSpace(line)).Should().HaveCount(3);
        _sink.TotalRejected.Should().Be(3);
        _sink.RejectedCountsBySymbol.Should().ContainKey("AAPL").WhoseValue.Should().Be(2);
        _sink.RejectedCountsBySymbol.Should().ContainKey("MSFT").WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task RecordAsync_AppendsOnlyTheNewRecord_NotTheWholeFile()
    {
        var batches = new List<int>();
        await using var sink = new DeadLetterSink(_testDir, logger: null, (_, lines, _) =>
        {
            batches.Add(lines.Count);
            return Task.CompletedTask;
        });

        for (var index = 0; index < 5; index++)
            await sink.RecordAsync(CreateTestEvent("AAPL"), ["invalid_sequence"]);

        // The previous implementation re-wrote every earlier record on each call.
        batches.Should().Equal(1, 1, 1, 1, 1);
    }

    [Fact]
    public async Task RecordAsync_WriteFails_RetainsTheRecordAndWritesItOnFlush()
    {
        var failWrites = true;
        var written = new List<string>();
        await using var sink = new DeadLetterSink(_testDir, logger: null, (_, lines, _) =>
        {
            if (failWrites)
                throw new IOException("disk unavailable");
            written.AddRange(lines);
            return Task.CompletedTask;
        });

        await sink.RecordAsync(CreateTestEvent("TSLA"), ["pipeline-persist-failure"]);

        sink.TotalRejected.Should().Be(1);
        sink.TotalPersisted.Should().Be(0, "a failed write must not be counted as recorded");
        sink.PendingRetryCount.Should().Be(1, "the dead letter can be the last copy of the event");
        sink.TotalLost.Should().Be(0);

        failWrites = false;
        await sink.FlushAsync();

        written.Should().ContainSingle().Which.Should().Contain("TSLA");
        var statistics = sink.GetStatistics();
        statistics.TotalPersisted.Should().Be(1);
        statistics.PendingRetry.Should().Be(0);
        statistics.TotalLost.Should().Be(0);
    }

    [Fact]
    public async Task RecordAsync_WriteFails_NextRecordRetriesEarlierOnesInOrder()
    {
        var failWrites = true;
        var written = new List<string>();
        await using var sink = new DeadLetterSink(
            _testDir,
            logger: null,
            (_, lines, _) =>
            {
                if (failWrites)
                    throw new IOException("disk unavailable");
                written.AddRange(lines);
                return Task.CompletedTask;
            },
            retryBackoff: TimeSpan.Zero);

        await sink.RecordAsync(CreateTestEvent("AAPL"), ["first"]);
        failWrites = false;
        await sink.RecordAsync(CreateTestEvent("MSFT"), ["second"]);

        written.Should().HaveCount(2);
        written[0].Should().Contain("AAPL");
        written[1].Should().Contain("MSFT");
        sink.TotalPersisted.Should().Be(2);
    }

    [Fact]
    public async Task DisposeAsync_WriteStillFailing_ReportsTheRecordAsLost()
    {
        var sink = new DeadLetterSink(_testDir, logger: null, (_, _, _) => throw new IOException("disk unavailable"));
        await sink.RecordAsync(CreateTestEvent("TSLA"), ["pipeline-persist-failure"]);

        await sink.DisposeAsync();

        sink.TotalLost.Should().Be(1);
        sink.PendingRetryCount.Should().Be(0);
    }

    [Fact]
    public async Task RecordAsync_RetryBufferFull_CountsFurtherRecordsAsLost()
    {
        await using var sink = new DeadLetterSink(_testDir, logger: null, (_, _, _) => throw new IOException("disk unavailable"));

        for (var index = 0; index < DeadLetterSink.MaxRetainedRecords + 3; index++)
            await sink.RecordAsync(CreateTestEvent("AAPL"), ["invalid_sequence"]);

        sink.TotalRejected.Should().Be(DeadLetterSink.MaxRetainedRecords + 3);
        sink.PendingRetryCount.Should().Be(DeadLetterSink.MaxRetainedRecords);
        sink.TotalLost.Should().Be(3);
    }

    [Fact]
    public async Task RecordAsync_BufferFullThenFileWritableAgain_NextRecordDrainsTheBacklog()
    {
        // Nothing flushes the sink while the pipeline runs. A full retry buffer used to turn every
        // later record away before the append that retries, so the backlog stayed in memory and
        // new dead letters kept being lost until shutdown, even once the file was writable again.
        var failWrites = true;
        var written = 0;
        await using var sink = new DeadLetterSink(
            _testDir,
            logger: null,
            (_, lines, _) =>
            {
                if (failWrites)
                    throw new IOException("disk unavailable");
                written += lines.Count;
                return Task.CompletedTask;
            },
            retryBackoff: TimeSpan.Zero);
        for (var index = 0; index < DeadLetterSink.MaxRetainedRecords + 1; index++)
            await sink.RecordAsync(CreateTestEvent("AAPL"), ["invalid_sequence"]);
        sink.PendingRetryCount.Should().Be(DeadLetterSink.MaxRetainedRecords);
        sink.TotalLost.Should().Be(1);

        failWrites = false;
        await sink.RecordAsync(CreateTestEvent("MSFT"), ["invalid_source"]);

        written.Should().Be(DeadLetterSink.MaxRetainedRecords + 1);
        sink.TotalPersisted.Should().Be(DeadLetterSink.MaxRetainedRecords + 1);
        sink.PendingRetryCount.Should().Be(0);
        sink.TotalLost.Should().Be(1, "the record that arrived after recovery is written, not lost");
    }

    private static MarketEvent CreateTestEvent(string symbol)
    {
        return new MarketEvent(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Type: MarketEventType.Trade,
            Payload: new Meridian.Contracts.Domain.Events.MarketEventPayload.HeartbeatPayload(),
            Sequence: 1,
            Source: "TEST");
    }
}
