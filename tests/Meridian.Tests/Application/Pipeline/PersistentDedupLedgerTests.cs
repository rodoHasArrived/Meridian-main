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

    [Fact]
    public async Task IsDuplicateAsync_FlushOnWriteTrue_PersistsRecordBeforeDisposeOrFlush()
    {
        var evt = CreateTradeEvent("AAPL", 1);

        _firstLedger = new PersistentDedupLedger(_ledgerDirectory, flushOnWrite: true);
        await _firstLedger.InitializeAsync();

        var firstSeen = await _firstLedger.IsDuplicateAsync(evt, CancellationToken.None);
        firstSeen.Should().BeFalse();

        // No FlushAsync, no DisposeAsync. Under strict durability the dedup record must
        // already be on disk before IsDuplicateAsync reported the event as new — otherwise a
        // crash here would re-admit the (already-emitted) event as new on restart.
        var ledgerPath = Path.Combine(_ledgerDirectory, "dedup_ledger.jsonl");
        var lines = await File.ReadAllLinesAsync(ledgerPath);
        lines.Should().ContainSingle();
        lines[0].Should().Contain("\"k\":\"TEST:AAPL:Trade:");
    }

    [Fact]
    public async Task IsDuplicateAsync_FlushOnWriteFalse_RecordStaysBufferedUntilFlush()
    {
        var evt = CreateTradeEvent("MSFT", 1);

        _firstLedger = new PersistentDedupLedger(_ledgerDirectory, flushOnWrite: false);
        await _firstLedger.InitializeAsync();

        var firstSeen = await _firstLedger.IsDuplicateAsync(evt, CancellationToken.None);
        firstSeen.Should().BeFalse();

        // Default (throughput) mode: the record is still in the writer buffer, not yet on disk.
        // The append-mode file handle creates an empty file, but no line has been flushed.
        var ledgerPath = Path.Combine(_ledgerDirectory, "dedup_ledger.jsonl");
        var bufferedLines = File.Exists(ledgerPath)
            ? await File.ReadAllLinesAsync(ledgerPath)
            : Array.Empty<string>();
        bufferedLines.Should().BeEmpty();

        // An explicit flush makes the buffered record durable, confirming it was buffered (not lost).
        await _firstLedger.FlushAsync(CancellationToken.None);
        var flushedLines = await File.ReadAllLinesAsync(ledgerPath);
        flushedLines.Should().ContainSingle();
    }

    [Fact]
    public async Task IsDuplicateAsync_PersistenceFails_RollsBackCacheSoEventIsNotDropped()
    {
        var evt = CreateTradeEvent("NVDA", 1);

        _firstLedger = new PersistentDedupLedger(_ledgerDirectory);
        await _firstLedger.InitializeAsync();

        // Force the persistence path to fail with a cancelled token. The optimistic cache
        // insert must be rolled back so the event is not silently treated as a duplicate.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var act = async () => await _firstLedger.IsDuplicateAsync(evt, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Re-checking the same event with a live token must still report it as new (not dropped).
        var seenAfterFailure = await _firstLedger.IsDuplicateAsync(evt, CancellationToken.None);
        seenAfterFailure.Should().BeFalse();
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
