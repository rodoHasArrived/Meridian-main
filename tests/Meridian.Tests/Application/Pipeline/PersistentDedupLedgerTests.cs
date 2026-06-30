using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
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

        var isDuplicate = await _firstLedger.IsDuplicateAsync(CreateTradeEvent("SPY", 1), CancellationToken.None);

        isDuplicate.Should().BeFalse();

        await _firstLedger.FlushAsync(CancellationToken.None);
        await _firstLedger.DisposeAsync();
        _firstLedger = null;

        var ledgerPath = Path.Combine(_ledgerDirectory, "dedup_ledger.jsonl");
        File.Exists(ledgerPath).Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(ledgerPath);
        lines.Should().ContainSingle();
    }

    [Fact]
    public async Task IsDuplicateAsync_TradeMiss_PersistsLineAndReloadsAsDuplicate()
    {
        var evt = CreateTradeEvent("AAPL", 1);

        _firstLedger = new PersistentDedupLedger(_ledgerDirectory);
        await _firstLedger.InitializeAsync();

        var firstSeen = await _firstLedger.IsDuplicateAsync(evt, CancellationToken.None);
        firstSeen.Should().BeFalse();

        await _firstLedger.FlushAsync(CancellationToken.None);
        await _firstLedger.DisposeAsync();
        _firstLedger = null;

        var ledgerPath = Path.Combine(_ledgerDirectory, "dedup_ledger.jsonl");
        File.Exists(ledgerPath).Should().BeTrue();

        var lines = await File.ReadAllLinesAsync(ledgerPath);
        lines.Should().ContainSingle();
        lines[0].Should().Contain("\"k\":\"TEST:AAPL:Trade:");
        lines[0].Should().Contain("\"t\":");

        _secondLedger = new PersistentDedupLedger(_ledgerDirectory);
        await _secondLedger.InitializeAsync();

        var secondSeen = await _secondLedger.IsDuplicateAsync(evt, CancellationToken.None);
        secondSeen.Should().BeTrue();
    }

    private static MarketEvent CreateTradeEvent(string symbol, long sequence)
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: 100.25m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: sequence,
            StreamId: "TEST",
            Venue: "XNAS");

        return MarketEvent.Trade(
            DateTimeOffset.UtcNow,
            symbol,
            trade,
            sequence,
            "TEST");
    }
}
