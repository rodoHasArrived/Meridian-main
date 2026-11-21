using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Events;
using Xunit;

namespace Meridian.Tests.Application.Pipeline;

public sealed class DroppedEventAuditTrailTests : IAsyncLifetime
{
    private string _testDir = null!;
    private DroppedEventAuditTrail _trail = null!;

    public Task InitializeAsync()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"audit_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _trail = new DroppedEventAuditTrail(_testDir);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _trail.DisposeAsync();
        try
        { Directory.Delete(_testDir, recursive: true); }
        catch { /* cleanup best effort */ }
    }

    [Fact]
    public void TotalDropped_InitiallyZero()
    {
        _trail.TotalDropped.Should().Be(0);
    }

    [Fact]
    public async Task RecordDroppedEvent_IncrementsTotalDropped()
    {
        // Arrange
        var evt = CreateTestEvent("SPY");

        // Act
        await _trail.RecordDroppedEventAsync(evt, "backpressure_queue_full");

        // Assert
        _trail.TotalDropped.Should().Be(1);
    }

    [Fact]
    public async Task RecordDroppedEvent_TracksPerSymbol()
    {
        // Arrange & Act
        await _trail.RecordDroppedEventAsync(CreateTestEvent("SPY"), "backpressure");
        await _trail.RecordDroppedEventAsync(CreateTestEvent("SPY"), "backpressure");
        await _trail.RecordDroppedEventAsync(CreateTestEvent("AAPL"), "backpressure");

        // Assert
        var counts = _trail.DropCountsBySymbol;
        counts.Should().ContainKey("SPY").WhoseValue.Should().Be(2);
        counts.Should().ContainKey("AAPL").WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task RecordDroppedEvent_WritesToAuditFile()
    {
        // Arrange
        var evt = CreateTestEvent("TSLA");

        // Act
        await _trail.RecordDroppedEventAsync(evt, "queue_full");
        await _trail.DisposeAsync(); // flush

        // Assert
        var auditFile = Path.Combine(_testDir, "_audit", "dropped_events.jsonl");
        File.Exists(auditFile).Should().BeTrue();
        var content = await File.ReadAllTextAsync(auditFile);
        content.Should().Contain("TSLA");
        content.Should().Contain("queue_full");
    }

    [Fact]
    public async Task GetStatistics_ReturnsCorrectSnapshot()
    {
        // Arrange
        await _trail.RecordDroppedEventAsync(CreateTestEvent("SPY"), "backpressure");
        await _trail.RecordDroppedEventAsync(CreateTestEvent("AAPL"), "backpressure");

        // Act
        var stats = _trail.GetStatistics();

        // Assert
        stats.TotalDropped.Should().Be(2);
        stats.DropsBySymbol.Should().HaveCount(2);
        stats.AuditFilePath.Should().Contain("dropped_events.jsonl");
        stats.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RecordDroppedEvent_MultipleEvents_AllWrittenToFile()
    {
        // Arrange & Act
        for (int i = 0; i < 5; i++)
        {
            await _trail.RecordDroppedEventAsync(CreateTestEvent($"SYM{i}"), "test_reason");
        }
        await _trail.DisposeAsync(); // flush

        // Assert
        var auditFile = Path.Combine(_testDir, "_audit", "dropped_events.jsonl");
        var lines = await File.ReadAllLinesAsync(auditFile);
        lines.Where(l => !string.IsNullOrWhiteSpace(l)).Should().HaveCount(5);
        _trail.TotalDropped.Should().Be(5);
    }

    [Fact]
    public async Task RecordDroppedEvent_AppendsOnlyTheNewRecord_NotTheWholeFile()
    {
        var batches = new List<int>();
        await using var trail = new DroppedEventAuditTrail(_testDir, logger: null, (_, lines, _) =>
        {
            batches.Add(lines.Count);
            return Task.CompletedTask;
        });

        for (var index = 0; index < 5; index++)
            await trail.RecordDroppedEventAsync(CreateTestEvent("SPY"), "backpressure_queue_full");

        // The previous implementation re-wrote every earlier record on each call.
        batches.Should().Equal(1, 1, 1, 1, 1);
    }

    [Fact]
    public async Task RecordDroppedEvent_DiskSlowerThanDrops_BoundsPendingRecordsAndStillCountsEveryDrop()
    {
        // The pipeline records drops without awaiting them. With the disk stalled, every record
        // used to pile up behind the write lock without limit.
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var written = 0;
        await using var trail = new DroppedEventAuditTrail(_testDir, logger: null, async (_, lines, _) =>
        {
            await release.Task;
            Interlocked.Add(ref written, lines.Count);
        });
        const int overflow = 250;
        var total = DroppedEventAuditTrail.MaxPendingRecords + overflow;

        var pending = new List<Task>(total);
        for (var index = 0; index < total; index++)
            pending.Add(trail.RecordDroppedEventAsync(CreateTestEvent("SPY"), "backpressure_queue_full").AsTask());

        trail.TotalDropped.Should().Be(total, "every drop is counted even when its audit line is skipped");
        trail.AuditRecordsNotWritten.Should().Be(overflow);
        pending.Count(task => !task.IsCompleted).Should().Be(DroppedEventAuditTrail.MaxPendingRecords);

        release.SetResult();
        await Task.WhenAll(pending);

        written.Should().Be(DroppedEventAuditTrail.MaxPendingRecords);
        trail.GetStatistics().AuditRecordsNotWritten.Should().Be(overflow);
    }

    [Fact]
    public async Task RecordDroppedEvent_WriteFails_CountsTheDropAndReportsTheMissingAuditLine()
    {
        await using var trail = new DroppedEventAuditTrail(_testDir, logger: null, (_, _, _) => throw new IOException("disk unavailable"));

        await trail.RecordDroppedEventAsync(CreateTestEvent("SPY"), "backpressure_queue_full");

        trail.TotalDropped.Should().Be(1);
        trail.AuditRecordsNotWritten.Should().Be(1);
    }

    [Fact]
    public void Constructor_ThrowsOnNullOrEmptyDirectory()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new DroppedEventAuditTrail(""));
        Assert.Throws<ArgumentException>(() => new DroppedEventAuditTrail("  "));
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
