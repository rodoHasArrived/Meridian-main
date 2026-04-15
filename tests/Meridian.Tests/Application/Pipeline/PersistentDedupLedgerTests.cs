using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Domain.Events;
using Xunit;

namespace Meridian.Tests.Application.Pipeline;

public sealed class PersistentDedupLedgerTests : IAsyncLifetime
{
    private string _ledgerDirectory = null!;
    private PersistentDedupLedger? _firstLedger;
    private PersistentDedupLedger? _secondLedger;

    public Task InitializeAsync()
    {
        _ledgerDirectory = Path.Combine(Path.GetTempPath(), $"dedup_ledger_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_ledgerDirectory);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_secondLedger is not null)
        {
            await _secondLedger.DisposeAsync();
        }

        if (_firstLedger is not null)
        {
            await _firstLedger.DisposeAsync();
        }

        try
        {
            if (Directory.Exists(_ledgerDirectory))
            {
                Directory.Delete(_ledgerDirectory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for transient file handles on Windows.
        }
    }

    [Fact]
    public async Task InitializeAsync_SecondIdleLedger_DoesNotBlockFirstWriter()
    {
        _firstLedger = new PersistentDedupLedger(_ledgerDirectory);
        await _firstLedger.InitializeAsync();

        _secondLedger = new PersistentDedupLedger(_ledgerDirectory);
        await _secondLedger.InitializeAsync();

        var isDuplicate = await _firstLedger.IsDuplicateAsync(CreateTestEvent("SPY"), CancellationToken.None);

        isDuplicate.Should().BeFalse();

        await _firstLedger.FlushAsync(CancellationToken.None);
        await _firstLedger.DisposeAsync();
        _firstLedger = null;

        var ledgerPath = Path.Combine(_ledgerDirectory, "dedup_ledger.jsonl");
        File.Exists(ledgerPath).Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(ledgerPath);
        lines.Should().ContainSingle();
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
