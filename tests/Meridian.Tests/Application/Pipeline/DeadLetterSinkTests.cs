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
